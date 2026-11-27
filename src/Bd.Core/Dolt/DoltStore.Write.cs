using System.Buffers;
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Bd.Core.Abstractions;
using Bd.Core.BdCli;
using Bd.Core.Domain;
using MySqlConnector;

namespace Bd.Core.Dolt;

/// <summary>
/// 写路径：直接操作 bd 的 Dolt 表（issues / dependencies / labels / comments / events / leases），
/// 每次写入后执行一次 <c>DOLT_COMMIT('-Am', ...)</c>，落库格式与 bd CLI 保持兼容。
/// </summary>
public sealed partial class DoltStore
{
    private const int LeaseMinutes = 5;
    private const int IdSuffixLength = 3;
    private const int IdAllocationAttempts = 24;
    private const string IdAlphabet = "0123456789abcdefghijklmnopqrstuvwxyz";
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    private static readonly string[] KnownStatuses =
        ["open", "in_progress", "closed", "blocked", "deferred", "pinned"];

    private static readonly JsonWriterOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false
    };

    private string? _actor;

    // ---------------------------------------------------------------- 创建

    public async Task<string> CreateAsync(CreateBeadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var title = request.Title?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            throw new BdException(BdErrorCode.InvalidArgument, "任务标题不能为空");
        }

        var type = string.IsNullOrWhiteSpace(request.Type) ? "task" : request.Type.Trim();
        var priority = Math.Clamp(request.Priority, 0, 4);
        var description = request.Description?.Trim() ?? string.Empty;
        var assignee = Normalize(request.Assignee);
        var parentId = Normalize(request.ParentId);
        var labels = NormalizeLabels(request.Labels);
        var created = string.Empty;

        var result = await WriteAsync<string>(
            () => $"ui: create {created}",
            cancellationToken,
            async (connection, transaction, actor, ct) =>
            {
                created = await AllocateIdAsync(connection, transaction, parentId, ct).ConfigureAwait(false);
                var now = UtcNow();

                await ExecuteAsync(
                    connection,
                    transaction,
                    """
                    INSERT INTO issues
                        (id, title, description, design, acceptance_criteria, notes, status, priority,
                         issue_type, assignee, created_at, created_by, owner, updated_at, is_blocked, row_lock)
                    VALUES
                        (@id, @title, @description, '', '', '', 'open', @priority,
                         @issue_type, @assignee, @created_at, @actor, '', @updated_at, 0, @row_lock)
                    """,
                    ct,
                    ("@id", created),
                    ("@title", title),
                    ("@description", description),
                    ("@priority", priority),
                    ("@issue_type", type),
                    ("@assignee", assignee),
                    ("@created_at", now),
                    ("@updated_at", now),
                    ("@actor", actor),
                    ("@row_lock", NewRowLock())).ConfigureAwait(false);

                if (parentId is not null)
                {
                    await ExecuteAsync(
                        connection,
                        transaction,
                        """
                        INSERT INTO dependencies (id, issue_id, type, created_at, created_by, thread_id, depends_on_issue_id)
                        VALUES (@id, @issue_id, 'parent-child', @created_at, @created_by, '', @parent)
                        """,
                        ct,
                        ("@id", Guid.NewGuid().ToString()),
                        ("@issue_id", created),
                        ("@created_at", now),
                        ("@created_by", actor),
                        ("@parent", parentId)).ConfigureAwait(false);
                }

                foreach (var label in labels)
                {
                    await ExecuteAsync(
                        connection,
                        transaction,
                        "INSERT IGNORE INTO labels (issue_id, label) VALUES (@issue_id, @label)",
                        ct,
                        ("@issue_id", created),
                        ("@label", label)).ConfigureAwait(false);
                }

                // bd 创建任务的 created 事件 old/new 都为空。
                await InsertEventAsync(connection, transaction, created, "created", actor, null, null, null, now, ct)
                    .ConfigureAwait(false);

                return created;
            }).ConfigureAwait(false);

        return result ?? created;
    }

    // ---------------------------------------------------------------- 更新

    public async Task UpdateAsync(string id, UpdateBeadRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(request);
        if (!request.HasChanges)
        {
            return;
        }

        await WriteAsync(
            () => $"ui: update {id}",
            cancellationToken,
            async (connection, transaction, actor, ct) =>
            {
                var snapshot = await ReadSnapshotAsync(connection, transaction, id, ct).ConfigureAwait(false)
                    ?? throw BdException.NotFound(id);

                var now = UtcNow();
                // content_hash 由 bd 自算，我们改动内容后置 NULL，避免留下与内容不符的旧哈希。
                var assignments = new List<string> { "content_hash = NULL", "updated_at = @updated_at", "row_lock = @row_lock" };
                var parameters = new List<(string, object?)>
                {
                    ("@updated_at", now),
                    ("@row_lock", NewRowLock()),
                    ("@id", id),
                    ("@old_row_lock", snapshot.RowLock)
                };
                var changes = new List<KeyValuePair<string, object?>>();

                if (request.Title is { } requestedTitle)
                {
                    var title = requestedTitle.Trim();
                    if (title.Length == 0)
                    {
                        throw new BdException(BdErrorCode.InvalidArgument, "任务标题不能为空");
                    }

                    if (!string.Equals(title, snapshot.Title, StringComparison.Ordinal))
                    {
                        assignments.Add("title = @title");
                        parameters.Add(("@title", title));
                        changes.Add(new("title", title));
                    }
                }

                if (request.Description is { } requestedDescription)
                {
                    var description = requestedDescription.Trim();
                    if (!string.Equals(description, snapshot.Description, StringComparison.Ordinal))
                    {
                        assignments.Add("description = @description");
                        parameters.Add(("@description", description));
                        changes.Add(new("description", description));
                    }
                }

                if (request.Type is { } requestedType)
                {
                    var type = requestedType.Trim();
                    if (type.Length == 0)
                    {
                        throw new BdException(BdErrorCode.InvalidArgument, "任务类型不能为空");
                    }

                    if (!string.Equals(type, snapshot.IssueType, StringComparison.Ordinal))
                    {
                        assignments.Add("issue_type = @issue_type");
                        parameters.Add(("@issue_type", type));
                        changes.Add(new("issue_type", type));
                    }
                }

                if (request.Priority is { } requestedPriority)
                {
                    var priority = Math.Clamp(requestedPriority, 0, 4);
                    if (priority != snapshot.Priority)
                    {
                        assignments.Add("priority = @priority");
                        parameters.Add(("@priority", priority));
                        changes.Add(new("priority", priority));
                    }
                }

                var assignee = Normalize(request.Assignee);
                if (request.Assignee is not null && !string.Equals(assignee, snapshot.Assignee, StringComparison.Ordinal))
                {
                    assignments.Add("assignee = @assignee");
                    parameters.Add(("@assignee", assignee));
                    changes.Add(new("assignee", assignee ?? string.Empty));
                }

                if (!string.IsNullOrWhiteSpace(request.Notes))
                {
                    var notes = request.Notes.Trim();
                    // 对齐 bd --append-notes：单换行拼接，已有内容非空时前置一个换行。
                    assignments.Add("notes = TRIM(CONCAT_WS('\\n', NULLIF(notes, ''), @notes))");
                    parameters.Add(("@notes", notes));
                    changes.Add(new("notes", notes));
                }

                var status = snapshot.Status;
                var statusChanged = false;
                if (request.Status is { } requestedStatus)
                {
                    status = requestedStatus.Trim().ToLowerInvariant();
                    if (!KnownStatuses.Contains(status, StringComparer.Ordinal))
                    {
                        throw new BdException(BdErrorCode.InvalidArgument, $"未知状态：{requestedStatus}");
                    }

                    if (!string.Equals(status, snapshot.Status, StringComparison.Ordinal))
                    {
                        statusChanged = true;
                        assignments.Add("status = @status");
                        parameters.Add(("@status", status));
                        changes.Add(new("status", status));

                        if (status == "closed")
                        {
                            assignments.Add("closed_at = @closed_at");
                            assignments.Add("closed_by_session = @closed_by_session");
                            assignments.Add("close_reason = ''");
                            parameters.Add(("@closed_at", now));
                            parameters.Add(("@closed_by_session", NewSessionId()));
                        }
                        else if (string.Equals(snapshot.Status, "closed", StringComparison.Ordinal))
                        {
                            assignments.Add("closed_at = NULL");
                            assignments.Add("closed_by_session = NULL");
                            assignments.Add("close_reason = ''");
                        }

                        if (status == "in_progress")
                        {
                            assignments.Add("started_at = COALESCE(started_at, @started_at)");
                            parameters.Add(("@started_at", now));
                        }
                    }
                }

                if (changes.Count == 0)
                {
                    // 没有任何实际字段变化：回滚并跳过 dolt commit。
                    throw AbortWrite.Instance;
                }

                var affected = await ExecuteAsync(
                    connection,
                    transaction,
                    $"UPDATE issues SET {string.Join(", ", assignments)} WHERE id = @id AND row_lock = @old_row_lock",
                    ct,
                    [.. parameters]).ConfigureAwait(false);

                if (affected == 0)
                {
                    throw new BdException(BdErrorCode.LockBusy, "任务已被其他会话修改，请刷新后重试");
                }

                if (statusChanged)
                {
                    if (status == "in_progress")
                    {
                        var holder = assignee ?? snapshot.Assignee ?? actor;
                        await UpsertLeaseAsync(connection, transaction, id, holder, now, ct).ConfigureAwait(false);
                    }
                    else if (string.Equals(snapshot.Status, "in_progress", StringComparison.Ordinal))
                    {
                        await DeleteLeaseAsync(connection, transaction, id, ct).ConfigureAwait(false);
                    }
                }

                await InsertEventAsync(
                    connection,
                    transaction,
                    id,
                    statusChanged ? "status_changed" : "updated",
                    actor,
                    BuildSnapshotJson(snapshot),
                    BuildDeltaJson(changes),
                    null,
                    now,
                    ct).ConfigureAwait(false);

                if (statusChanged)
                {
                    await RecomputeBlockedAsync(connection, transaction, id, ct).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- 关闭

    public async Task CloseAsync(string id, string? reason, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var closeReason = reason?.Trim() ?? string.Empty;

        await WriteAsync(
            () => $"ui: close {id}",
            cancellationToken,
            async (connection, transaction, actor, ct) =>
            {
                var snapshot = await ReadSnapshotAsync(connection, transaction, id, ct).ConfigureAwait(false)
                    ?? throw BdException.NotFound(id);

                if (string.Equals(snapshot.Status, "closed", StringComparison.Ordinal))
                {
                    throw AbortWrite.Instance;
                }

                var now = UtcNow();
                var affected = await ExecuteAsync(
                    connection,
                    transaction,
                    """
                    UPDATE issues
                    SET status = 'closed', closed_at = @closed_at, closed_by_session = @session,
                        close_reason = @close_reason, content_hash = NULL,
                        updated_at = @updated_at, row_lock = @row_lock
                    WHERE id = @id AND row_lock = @old_row_lock
                    """,
                    ct,
                    ("@closed_at", now),
                    ("@session", NewSessionId()),
                    ("@close_reason", closeReason),
                    ("@updated_at", now),
                    ("@row_lock", NewRowLock()),
                    ("@id", id),
                    ("@old_row_lock", snapshot.RowLock)).ConfigureAwait(false);

                if (affected == 0)
                {
                    throw new BdException(BdErrorCode.LockBusy, "任务已被其他会话修改，请刷新后重试");
                }

                await DeleteLeaseAsync(connection, transaction, id, ct).ConfigureAwait(false);
                await InsertEventAsync(
                    connection,
                    transaction,
                    id,
                    "closed",
                    actor,
                    string.Empty,
                    closeReason,
                    null,
                    now,
                    ct).ConfigureAwait(false);
                await RecomputeBlockedAsync(connection, transaction, id, ct).ConfigureAwait(false);
            }).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- 删除

    public async Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        await WriteAsync(
            () => $"ui: delete {id}",
            cancellationToken,
            async (connection, transaction, _, ct) =>
            {
                await DeleteLeaseAsync(connection, transaction, id, ct).ConfigureAwait(false);
                var affected = await ExecuteAsync(
                    connection,
                    transaction,
                    "DELETE FROM issues WHERE id = @id",
                    ct,
                    ("@id", id)).ConfigureAwait(false);

                if (affected == 0)
                {
                    throw BdException.NotFound(id);
                }
            }).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- 评论

    public async Task AddCommentAsync(string id, string text, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var body = text?.Trim();
        if (string.IsNullOrEmpty(body))
        {
            throw new BdException(BdErrorCode.InvalidArgument, "评论内容不能为空");
        }

        await WriteAsync(
            () => $"ui: comment {id}",
            cancellationToken,
            async (connection, transaction, actor, ct) =>
            {
                await EnsureIssueExistsAsync(connection, transaction, id, ct).ConfigureAwait(false);
                var now = UtcNow();

                await ExecuteAsync(
                    connection,
                    transaction,
                    """
                    INSERT INTO comments (id, issue_id, author, text, created_at)
                    VALUES (@id, @issue_id, @author, @text, @created_at)
                    """,
                    ct,
                    ("@id", Guid.NewGuid().ToString()),
                    ("@issue_id", id),
                    ("@author", actor),
                    ("@text", body),
                    ("@created_at", now)).ConfigureAwait(false);

                await TouchIssueAsync(connection, transaction, id, now, ct).ConfigureAwait(false);
            }).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- 标签

    public async Task AddLabelAsync(string id, string label, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var value = RequireLabel(label);

        await WriteAsync(
            () => $"ui: label {value} {id}",
            cancellationToken,
            async (connection, transaction, actor, ct) =>
            {
                await EnsureIssueExistsAsync(connection, transaction, id, ct).ConfigureAwait(false);
                var now = UtcNow();

                await ExecuteAsync(
                    connection,
                    transaction,
                    "INSERT IGNORE INTO labels (issue_id, label) VALUES (@issue_id, @label)",
                    ct,
                    ("@issue_id", id),
                    ("@label", value)).ConfigureAwait(false);

                await TouchIssueAsync(connection, transaction, id, now, ct).ConfigureAwait(false);
                await InsertEventAsync(
                    connection,
                    transaction,
                    id,
                    "label_added",
                    actor,
                    null,
                    null,
                    $"Added label: {value}",
                    now,
                    ct).ConfigureAwait(false);
            }).ConfigureAwait(false);
    }

    public async Task RemoveLabelAsync(string id, string label, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var value = RequireLabel(label);

        await WriteAsync(
            () => $"ui: unlabel {value} {id}",
            cancellationToken,
            async (connection, transaction, actor, ct) =>
            {
                var removed = await ExecuteAsync(
                    connection,
                    transaction,
                    "DELETE FROM labels WHERE issue_id = @issue_id AND label = @label",
                    ct,
                    ("@issue_id", id),
                    ("@label", value)).ConfigureAwait(false);

                if (removed == 0)
                {
                    throw AbortWrite.Instance;
                }

                var now = UtcNow();
                await TouchIssueAsync(connection, transaction, id, now, ct).ConfigureAwait(false);
                await InsertEventAsync(
                    connection,
                    transaction,
                    id,
                    "label_removed",
                    actor,
                    null,
                    null,
                    $"Removed label: {value}",
                    now,
                    ct).ConfigureAwait(false);
            }).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- 依赖

    public async Task AddDependencyAsync(string id, string dependsOnId, DependencyType type, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(dependsOnId);

        var target = dependsOnId.Trim();
        if (string.Equals(id.Trim(), target, StringComparison.Ordinal))
        {
            throw new BdException(BdErrorCode.InvalidArgument, "不能依赖自己");
        }

        var rawType = BdEnumMaps.ToBdString(type);

        await WriteAsync(
            () => $"ui: dep {id} {rawType} {target}",
            cancellationToken,
            async (connection, transaction, actor, ct) =>
            {
                await EnsureIssueExistsAsync(connection, transaction, id, ct).ConfigureAwait(false);
                var exists = await ScalarAsync(
                    connection,
                    transaction,
                    "SELECT 1 FROM issues WHERE id = @id LIMIT 1",
                    ct,
                    ("@id", target)).ConfigureAwait(false);

                if (exists is null)
                {
                    throw new BdException(BdErrorCode.NotFound, $"依赖的目标任务不存在：{target}");
                }

                var now = UtcNow();
                try
                {
                    await ExecuteAsync(
                        connection,
                        transaction,
                        """
                        INSERT INTO dependencies (id, issue_id, type, created_at, created_by, thread_id, depends_on_issue_id)
                        VALUES (@id, @issue_id, @type, @created_at, @created_by, '', @depends_on)
                        """,
                        ct,
                        ("@id", Guid.NewGuid().ToString()),
                        ("@issue_id", id),
                        ("@type", rawType),
                        ("@created_at", now),
                        ("@created_by", actor),
                        ("@depends_on", target)).ConfigureAwait(false);
                }
                catch (MySqlException exception) when (exception.Number == 1062)
                {
                    throw new BdException(BdErrorCode.InvalidArgument, $"依赖已存在：{id} → {target}", inner: exception);
                }

                await TouchIssueAsync(connection, transaction, id, now, ct).ConfigureAwait(false);
                await InsertEventAsync(
                    connection,
                    transaction,
                    id,
                    "dependency_added",
                    actor,
                    string.Empty,
                    $"Added dependency: {id} {rawType} {target}",
                    null,
                    now,
                    ct).ConfigureAwait(false);
                await RecomputeBlockedAsync(connection, transaction, id, ct).ConfigureAwait(false);
            }).ConfigureAwait(false);
    }

    public async Task RemoveDependencyAsync(string id, string dependsOnId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(dependsOnId);

        var target = dependsOnId.Trim();

        await WriteAsync(
            () => $"ui: undep {id} {target}",
            cancellationToken,
            async (connection, transaction, actor, ct) =>
            {
                var removed = await ExecuteAsync(
                    connection,
                    transaction,
                    "DELETE FROM dependencies WHERE issue_id = @issue_id AND depends_on_issue_id = @depends_on",
                    ct,
                    ("@issue_id", id),
                    ("@depends_on", target)).ConfigureAwait(false);

                if (removed == 0)
                {
                    throw new BdException(BdErrorCode.InvalidArgument, $"依赖不存在：{id} → {target}");
                }

                var now = UtcNow();
                await TouchIssueAsync(connection, transaction, id, now, ct).ConfigureAwait(false);
                await InsertEventAsync(
                    connection,
                    transaction,
                    id,
                    "dependency_removed",
                    actor,
                    string.Empty,
                    $"Removed dependency on {target}",
                    null,
                    now,
                    ct).ConfigureAwait(false);
                await RecomputeBlockedAsync(connection, transaction, id, ct).ConfigureAwait(false);
            }).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- 事务壳

    /// <summary>打开连接 → 开启事务 → 执行 → 提交 → DOLT_COMMIT；<see cref="AbortWrite"/> 表示无变化、跳过提交。</summary>
    private async Task<T?> WriteAsync<T>(
        Func<string> commitMessage,
        CancellationToken cancellationToken,
        Func<MySqlConnection, MySqlTransaction, string, CancellationToken, Task<T>> body)
        where T : class
    {
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            var actor = await ResolveActorAsync(connection, cancellationToken).ConfigureAwait(false);
            T result;

            try
            {
                await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                result = await body(connection, transaction, actor, cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (AbortWrite)
            {
                return null;
            }

            await DoltCommitAsync(connection, commitMessage(), cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception exception) when (exception is not BdException and not AbortWrite)
        {
            throw Translate(exception);
        }
    }

    private async Task WriteAsync(
        Func<string> commitMessage,
        CancellationToken cancellationToken,
        Func<MySqlConnection, MySqlTransaction, string, CancellationToken, Task> body)
    {
        await WriteAsync<object>(
            commitMessage,
            cancellationToken,
            async (connection, transaction, actor, ct) =>
            {
                await body(connection, transaction, actor, ct).ConfigureAwait(false);
                return new object();
            }).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- ID / 署名

    /// <summary>子任务走 child_counters 递增；顶层走 issue_counter（存在时）或 3 位随机后缀。</summary>
    private static async Task<string> AllocateIdAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string? parentId,
        CancellationToken cancellationToken)
    {
        if (parentId is not null)
        {
            var parent = await ScalarAsync(
                connection,
                transaction,
                "SELECT id FROM issues WHERE id = @id LIMIT 1",
                cancellationToken,
                ("@id", parentId)).ConfigureAwait(false)
                ?? throw new BdException(BdErrorCode.NotFound, $"父任务不存在：{parentId}");

            await ExecuteAsync(
                connection,
                transaction,
                """
                INSERT INTO child_counters (parent_id, last_child) VALUES (@parent, 1)
                ON DUPLICATE KEY UPDATE last_child = last_child + 1
                """,
                cancellationToken,
                ("@parent", parent)).ConfigureAwait(false);

            var lastChild = await ScalarAsync(
                connection,
                transaction,
                "SELECT last_child FROM child_counters WHERE parent_id = @parent",
                cancellationToken,
                ("@parent", parent)).ConfigureAwait(false);

            return $"{parent}.{Convert.ToInt32(lastChild, CultureInfo.InvariantCulture)}";
        }

        var prefix = await ScalarAsync(
            connection,
            transaction,
            "SELECT value FROM config WHERE `key` = 'issue_prefix' LIMIT 1",
            cancellationToken).ConfigureAwait(false) as string;

        if (string.IsNullOrWhiteSpace(prefix))
        {
            throw new BdException(BdErrorCode.NotARepo, "当前数据库不是 beads 项目（缺少 issue_prefix）");
        }

        var normalizedPrefix = prefix.Trim();
        var lastId = await ScalarAsync(
            connection,
            transaction,
            "SELECT last_id FROM issue_counter WHERE prefix = @prefix FOR UPDATE",
            cancellationToken,
            ("@prefix", normalizedPrefix)).ConfigureAwait(false);

        if (lastId is not null)
        {
            var next = Convert.ToInt32(lastId, CultureInfo.InvariantCulture) + 1;
            await ExecuteAsync(
                connection,
                transaction,
                "UPDATE issue_counter SET last_id = @last_id WHERE prefix = @prefix",
                cancellationToken,
                ("@last_id", next),
                ("@prefix", normalizedPrefix)).ConfigureAwait(false);
            return $"{normalizedPrefix}-{next}";
        }

        for (var attempt = 0; attempt < IdAllocationAttempts; attempt++)
        {
            var candidate = $"{normalizedPrefix}-{BuildIdSuffix(Random.Shared)}";
            var taken = await ScalarAsync(
                connection,
                transaction,
                "SELECT 1 FROM issues WHERE id = @id LIMIT 1",
                cancellationToken,
                ("@id", candidate)).ConfigureAwait(false);

            if (taken is null)
            {
                return candidate;
            }
        }

        throw new BdException(BdErrorCode.Unknown, "连续生成的任务 ID 都已被占用，请稍后重试");
    }

    internal static string BuildIdSuffix(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        return string.Create(IdSuffixLength, random, static (span, rng) =>
        {
            for (var index = 0; index < span.Length; index++)
            {
                span[index] = IdAlphabet[rng.Next(IdAlphabet.Length)];
            }
        });
    }

    /// <summary>署名优先取配置里的 HumanActor，其次库里最近一次 created_by，最后用系统用户名。</summary>
    private async Task<string> ResolveActorAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        if (_actor is not null)
        {
            return _actor;
        }

        if (!string.IsNullOrWhiteSpace(_settings.Actor))
        {
            _actor = _settings.Actor.Trim();
            return _actor;
        }

        var latest = await ScalarAsync(
            connection,
            null,
            "SELECT created_by FROM issues WHERE created_by IS NOT NULL AND created_by <> '' ORDER BY updated_at DESC LIMIT 1",
            cancellationToken).ConfigureAwait(false) as string;

        _actor = string.IsNullOrWhiteSpace(latest)
            ? string.IsNullOrWhiteSpace(Environment.UserName) ? "ui" : Environment.UserName.Trim()
            : latest.Trim();
        return _actor;
    }

    // ---------------------------------------------------------------- 事件 / 快照

    private static async Task InsertEventAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string issueId,
        string eventType,
        string actor,
        string? oldValue,
        string? newValue,
        string? comment,
        DateTime createdAt,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO events (id, issue_id, event_type, actor, old_value, new_value, comment, created_at)
            VALUES (@id, @issue_id, @event_type, @actor, @old_value, @new_value, @comment, @created_at)
            """,
            cancellationToken,
            ("@id", Guid.NewGuid().ToString()),
            ("@issue_id", issueId),
            ("@event_type", eventType),
            ("@actor", actor),
            ("@old_value", oldValue),
            ("@new_value", newValue),
            ("@comment", comment),
            ("@created_at", createdAt)).ConfigureAwait(false);

    /// <summary>按 bd 的事件快照形态生成 old_value：空字段不输出，时间用 UTC RFC3339。</summary>
    internal static string BuildSnapshotJson(IssueSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return BuildJson(writer =>
        {
            writer.WriteString("id", snapshot.Id);
            writer.WriteString("title", snapshot.Title);
            WriteOptionalString(writer, "description", snapshot.Description);
            WriteOptionalString(writer, "design", snapshot.Design);
            WriteOptionalString(writer, "acceptance_criteria", snapshot.AcceptanceCriteria);
            WriteOptionalString(writer, "notes", snapshot.Notes);
            writer.WriteString("status", snapshot.Status);
            writer.WriteNumber("priority", snapshot.Priority);
            writer.WriteString("issue_type", snapshot.IssueType);
            WriteOptionalString(writer, "assignee", snapshot.Assignee);
            WriteOptionalString(writer, "owner", snapshot.Owner);
            writer.WriteString("created_at", FormatTimestamp(snapshot.CreatedAt));
            writer.WriteString("created_by", snapshot.CreatedBy);
            writer.WriteString("updated_at", FormatTimestamp(snapshot.UpdatedAt));

            if (snapshot.StartedAt is { } startedAt)
            {
                writer.WriteString("started_at", FormatTimestamp(startedAt));
            }

            if (snapshot.ClosedAt is { } closedAt)
            {
                writer.WriteString("closed_at", FormatTimestamp(closedAt));
            }

            WriteOptionalString(writer, "close_reason", snapshot.CloseReason);

            if (snapshot.LeaseExpiresAt is { } leaseExpiresAt)
            {
                writer.WriteString("lease_expires_at", FormatTimestamp(leaseExpiresAt));
            }

            if (snapshot.HeartbeatAt is { } heartbeatAt)
            {
                writer.WriteString("heartbeat_at", FormatTimestamp(heartbeatAt));
            }
        });
    }

    /// <summary>生成事件 new_value：只含本次改动字段，按字段名排序（与 bd 一致）。</summary>
    internal static string BuildDeltaJson(IReadOnlyList<KeyValuePair<string, object?>> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        return BuildJson(writer =>
        {
            foreach (var change in changes.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                switch (change.Value)
                {
                    case null:
                        writer.WriteNull(change.Key);
                        break;
                    case int intValue:
                        writer.WriteNumber(change.Key, intValue);
                        break;
                    case long longValue:
                        writer.WriteNumber(change.Key, longValue);
                        break;
                    case bool boolValue:
                        writer.WriteBoolean(change.Key, boolValue);
                        break;
                    default:
                        writer.WriteString(
                            change.Key,
                            Convert.ToString(change.Value, CultureInfo.InvariantCulture) ?? string.Empty);
                        break;
                }
            }
        });
    }

    private static string BuildJson(Action<Utf8JsonWriter> writeBody)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, JsonOptions))
        {
            writer.WriteStartObject();
            writeBody(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteOptionalString(Utf8JsonWriter writer, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            writer.WriteString(name, value);
        }
    }

    internal static string FormatTimestamp(DateTime value) =>
        value.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture);

    private static async Task<IssueSnapshot?> ReadSnapshotAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string id,
        CancellationToken cancellationToken)
    {
        await using var command = CreateTxCommand(
            connection,
            transaction,
            """
            SELECT id, title, description, design, acceptance_criteria, notes, status, priority, issue_type,
                   assignee, owner, created_at, created_by, updated_at, started_at, closed_at,
                   closed_by_session, close_reason, row_lock
            FROM issues
            WHERE id = @id
            LIMIT 1
            FOR UPDATE
            """,
            ("@id", id));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var snapshot = new IssueSnapshot(
            ReadString(reader, "id") ?? string.Empty,
            ReadString(reader, "title") ?? string.Empty,
            ReadString(reader, "description") ?? string.Empty,
            ReadString(reader, "design") ?? string.Empty,
            ReadString(reader, "acceptance_criteria") ?? string.Empty,
            ReadString(reader, "notes") ?? string.Empty,
            ReadString(reader, "status") ?? "open",
            ReadInt32(reader, "priority") ?? 2,
            ReadString(reader, "issue_type") ?? "task",
            ReadString(reader, "assignee"),
            ReadString(reader, "owner"),
            ReadTimestamp(reader, "created_at")?.UtcDateTime ?? UtcNow(),
            ReadString(reader, "created_by") ?? string.Empty,
            ReadTimestamp(reader, "updated_at")?.UtcDateTime ?? UtcNow(),
            ReadTimestamp(reader, "started_at")?.UtcDateTime,
            ReadTimestamp(reader, "closed_at")?.UtcDateTime,
            ReadString(reader, "closed_by_session"),
            ReadString(reader, "close_reason"),
            ReadLong(reader, "row_lock"));

        await reader.DisposeAsync().ConfigureAwait(false);

        if (!string.Equals(snapshot.Status, "in_progress", StringComparison.Ordinal))
        {
            return snapshot;
        }

        await using var leaseCommand = CreateTxCommand(
            connection,
            transaction,
            "SELECT lease_expires_at, heartbeat_at FROM leases WHERE issue_id = @id",
            ("@id", id));
        await using var leaseReader = await leaseCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await leaseReader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return snapshot;
        }

        return snapshot with
        {
            LeaseExpiresAt = ReadTimestamp(leaseReader, "lease_expires_at")?.UtcDateTime,
            HeartbeatAt = ReadTimestamp(leaseReader, "heartbeat_at")?.UtcDateTime
        };
    }

    private static long ReadLong(IDataRecord row, string column)
    {
        var ordinal = row.GetOrdinal(column);
        if (row.IsDBNull(ordinal))
        {
            return 0;
        }

        var value = row.GetValue(ordinal);
        return value switch
        {
            long longValue => longValue,
            int intValue => intValue,
            _ => Convert.ToInt64(value, CultureInfo.InvariantCulture)
        };
    }

    // ---------------------------------------------------------------- 小工具

    private static async Task UpsertLeaseAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string issueId,
        string holder,
        DateTime now,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO leases (issue_id, holder, granted_at, lease_expires_at, heartbeat_at, granted_node)
            VALUES (@issue_id, @holder, @now, @expires_at, @now, '')
            ON DUPLICATE KEY UPDATE holder = VALUES(holder), lease_expires_at = VALUES(lease_expires_at),
                                    heartbeat_at = VALUES(heartbeat_at)
            """,
            cancellationToken,
            ("@issue_id", issueId),
            ("@holder", holder),
            ("@now", now),
            ("@expires_at", now.AddMinutes(LeaseMinutes))).ConfigureAwait(false);

    private static async Task DeleteLeaseAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string issueId,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(
            connection,
            transaction,
            "DELETE FROM leases WHERE issue_id = @issue_id",
            cancellationToken,
            ("@issue_id", issueId)).ConfigureAwait(false);

    /// <summary>重算受影响任务（自身 + 直接依赖它的任务）的 is_blocked 冗余列。</summary>
    private static async Task RecomputeBlockedAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string issueId,
        CancellationToken cancellationToken)
    {
        var dependents = new List<string>();

        await using (var command = CreateTxCommand(
            connection,
            transaction,
            "SELECT issue_id FROM dependencies WHERE depends_on_issue_id = @id AND type = 'blocks'",
            ("@id", issueId)))
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var value = ReadString(reader, "issue_id");
                if (!string.IsNullOrEmpty(value))
                {
                    dependents.Add(value);
                }
            }
        }

        dependents.Add(issueId);

        foreach (var candidate in dependents.Distinct(StringComparer.Ordinal))
        {
            // 与 bd 的 blocked_issues 视图同口径：自身已终态 → 0；否则存在未终态的 blocks 依赖 → 1。
            var blocking = await ScalarAsync(
                connection,
                transaction,
                """
                SELECT CASE WHEN i.status NOT IN ('closed', 'pinned')
                             AND i.status NOT IN (SELECT name FROM custom_statuses WHERE category IN ('done', 'frozen'))
                             AND EXISTS (
                                 SELECT 1 FROM dependencies d
                                 JOIN issues blocker ON blocker.id = d.depends_on_issue_id
                                 WHERE d.issue_id = i.id AND d.type = 'blocks'
                                   AND blocker.status NOT IN ('closed', 'pinned')
                                   AND blocker.status NOT IN (
                                       SELECT name FROM custom_statuses WHERE category IN ('done', 'frozen')))
                            THEN 1 ELSE 0 END
                FROM issues i
                WHERE i.id = @id
                """,
                cancellationToken,
                ("@id", candidate)).ConfigureAwait(false);

            var blocked = Convert.ToInt32(blocking ?? 0, CultureInfo.InvariantCulture) > 0;
            await ExecuteAsync(
                connection,
                transaction,
                "UPDATE issues SET is_blocked = @blocked WHERE id = @id",
                cancellationToken,
                ("@blocked", blocked ? 1 : 0),
                ("@id", candidate)).ConfigureAwait(false);
        }
    }

    private static async Task EnsureIssueExistsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string id,
        CancellationToken cancellationToken)
    {
        var exists = await ScalarAsync(
            connection,
            transaction,
            "SELECT 1 FROM issues WHERE id = @id LIMIT 1",
            cancellationToken,
            ("@id", id)).ConfigureAwait(false);

        if (exists is null)
        {
            throw BdException.NotFound(id);
        }
    }

    private static async Task TouchIssueAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string id,
        DateTime now,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(
            connection,
            transaction,
            "UPDATE issues SET updated_at = @updated_at, row_lock = @row_lock, content_hash = NULL WHERE id = @id",
            cancellationToken,
            ("@updated_at", now),
            ("@row_lock", NewRowLock()),
            ("@id", id)).ConfigureAwait(false);

    private static async Task<int> ExecuteAsync(
        MySqlConnection connection,
        MySqlTransaction? transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = CreateTxCommand(connection, transaction, sql, parameters);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<object?> ScalarAsync(
        MySqlConnection connection,
        MySqlTransaction? transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = CreateTxCommand(connection, transaction, sql, parameters);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is DBNull ? null : value;
    }

    private static MySqlCommand CreateTxCommand(
        MySqlConnection connection,
        MySqlTransaction? transaction,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.CommandTimeout = 30;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.Add(new MySqlParameter(name, value ?? DBNull.Value));
        }

        return command;
    }

    private static async Task DoltCommitAsync(
        MySqlConnection connection,
        string message,
        CancellationToken cancellationToken)
    {
        await using var command = CreateTxCommand(connection, null, "CALL DOLT_COMMIT('-Am', @message)", ("@message", message));
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (MySqlException exception) when (exception.Message.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase))
        {
            // 数据未产生实际变更时 dolt 会拒绝空提交，忽略即可。
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string RequireLabel(string? label)
    {
        var value = label?.Trim();
        return string.IsNullOrEmpty(value)
            ? throw new BdException(BdErrorCode.InvalidArgument, "标签不能为空")
            : value;
    }

    private static IReadOnlyList<string> NormalizeLabels(IReadOnlyList<string>? labels) =>
        labels is null
            ? []
            : [.. labels.Select(static label => label?.Trim())
                .Where(static label => !string.IsNullOrEmpty(label))
                .Select(static label => label!)
                .Distinct(StringComparer.Ordinal)];

    private static long NewRowLock() => Random.Shared.NextInt64(1, long.MaxValue);

    private static string NewSessionId() => Guid.NewGuid().ToString("N");

    private static DateTime UtcNow() => Truncate(DateTime.UtcNow);

    private static DateTime Truncate(DateTime value) =>
        new(value.Ticks - (value.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);

    /// <summary>事务内无实际改动时的短路信号：回滚并跳过 dolt commit。</summary>
    private sealed class AbortWrite : Exception
    {
        public static readonly AbortWrite Instance = new();

        private AbortWrite()
            : base("write aborted without changes")
        {
        }
    }
}

/// <summary>写入前的任务快照，用于生成 events.old_value。</summary>
internal sealed record IssueSnapshot(
    string Id,
    string Title,
    string Description,
    string Design,
    string AcceptanceCriteria,
    string Notes,
    string Status,
    int Priority,
    string IssueType,
    string? Assignee,
    string? Owner,
    DateTime CreatedAt,
    string CreatedBy,
    DateTime UpdatedAt,
    DateTime? StartedAt,
    DateTime? ClosedAt,
    string? ClosedBySession,
    string? CloseReason,
    long RowLock)
{
    public DateTime? LeaseExpiresAt { get; init; }

    public DateTime? HeartbeatAt { get; init; }
}
