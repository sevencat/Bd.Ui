using Bd.Core.Abstractions;
using Bd.Core.Domain;

namespace Bd.Core.Dolt;

/// <summary>聚合模式下的一个成员：数据库名 + 该库的存储实例。</summary>
public sealed record ProjectStore(string Database, IBdStore Store);

/// <summary>
/// 把多个 <see cref="DoltStore"/> 合并成一个 <see cref="IBdStore"/>：<c>List/Ready/Blocked</c>
/// 并发拉取后打上 <see cref="Bead.Project"/> 合并；<c>Get/Dependencies/Comments</c> 按 issue ID
/// 路由到所属库；写操作一律拒绝（聚合模式只读）。
/// </summary>
public sealed class CompositeBdStore : IBdStore
{
    /// <summary>聚合模式在配置与下拉中的占位库名。</summary>
    public const string AggregateDatabase = "*";

    public const string ReadOnlyReason = "聚合模式只读：请在项目下拉中选择单个项目后再写入";

    private readonly Func<CancellationToken, Task<IReadOnlyList<ProjectStore>>> _resolve;
    private readonly DoltConnectionSettings _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile IReadOnlyList<ProjectStore>? _members;

    internal CompositeBdStore(
        DoltConnectionSettings settings,
        Func<CancellationToken, Task<IReadOnlyList<ProjectStore>>> resolve)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
    }

    /// <summary>按服务器上现有项目自动装配成员（首次读取时枚举，之后复用）。</summary>
    public static CompositeBdStore ForServer(
        DoltConnectionFactory factory,
        DoltConnectionSettings settings,
        ProjectCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(catalog);

        return new CompositeBdStore(settings, async cancellationToken =>
        {
            var projects = await catalog.ListAsync(settings, cancellationToken).ConfigureAwait(false);
            return
            [
                .. projects.Select(project => new ProjectStore(
                    project.Database,
                    new DoltStore(factory, settings.WithDatabase(project.Database))))
            ];
        });
    }

    public string BackendName => DoltStore.BackendNameValue;

    /// <summary>已解析的成员；尚未读取过时为当前缓存内容（可能为空）。</summary>
    public IReadOnlyList<ProjectStore> Members => _members ?? [];

    public async Task<IReadOnlyList<Bead>> ListAsync(BeadListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await MergeAsync(member => member.Store.ListAsync(query, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<IReadOnlyList<Bead>> ReadyAsync(CancellationToken cancellationToken) =>
        MergeAsync(member => member.Store.ReadyAsync(cancellationToken), cancellationToken);

    public Task<IReadOnlyList<Bead>> BlockedAsync(CancellationToken cancellationToken) =>
        MergeAsync(member => member.Store.BlockedAsync(cancellationToken), cancellationToken);

    public async Task<Bead> GetAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var owner = await FindOwnerAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw BdException.NotFound(id);

        var bead = await owner.Store.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return bead with { Project = owner.Database };
    }

    public async Task<IReadOnlyList<DependencyLink>> ListDependenciesAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var owner = await FindOwnerAsync(id, cancellationToken).ConfigureAwait(false);
        return owner is null
            ? []
            : await owner.Store.ListDependenciesAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Comment>> ListCommentsAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var owner = await FindOwnerAsync(id, cancellationToken).ConfigureAwait(false);
        return owner is null
            ? []
            : await owner.Store.ListCommentsAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> GetVersionAsync(CancellationToken cancellationToken)
    {
        var members = await EnsureMembersAsync(cancellationToken).ConfigureAwait(false);
        var versions = new List<string>();
        foreach (var member in members)
        {
            try
            {
                var version = await member.Store.GetVersionAsync(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(version) && !versions.Contains(version, StringComparer.Ordinal))
                {
                    versions.Add(version);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 单个库不可用不影响整体版本展示。
            }
        }

        return versions.Count == 0 ? string.Empty : string.Join(" / ", versions);
    }

    public async Task<DoctorReport> RunDoctorAsync(CancellationToken cancellationToken)
    {
        var members = await EnsureMembersAsync(cancellationToken).ConfigureAwait(false);
        var issues = new List<string>();
        var total = 0;
        var ok = members.Count > 0;
        var versions = new List<string>();

        foreach (var member in members)
        {
            try
            {
                var report = await member.Store.RunDoctorAsync(cancellationToken).ConfigureAwait(false);
                total += ParseIssueCount(report.RawOutput);
                if (!string.IsNullOrWhiteSpace(report.BdVersion) && !versions.Contains(report.BdVersion, StringComparer.Ordinal))
                {
                    versions.Add(report.BdVersion);
                }

                if (!report.Ok)
                {
                    ok = false;
                    issues.AddRange(report.Issues.Select(issue => $"{member.Database}: {issue}"));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ok = false;
                issues.Add($"{member.Database}: {ex.Message}");
            }
        }

        if (members.Count == 0)
        {
            issues.Add("服务器上没有任何 beads 项目");
        }

        return new DoctorReport
        {
            Ok = ok,
            BdVersion = string.Join(" / ", versions),
            RepoPath = $"dolt://{_settings.Host}:{_settings.Port}/{AggregateDatabase}",
            Summary = members.Count == 0
                ? "聚合模式：未发现项目"
                : $"聚合 {members.Count} 个项目 / {total} 条 issue" + (ok ? string.Empty : $"（{issues.Count} 项问题）"),
            RawOutput = $"databases={string.Join(",", members.Select(static member => member.Database))} issues={total}",
            Issues = issues
        };
    }

    public Task<string> CreateAsync(CreateBeadRequest request, CancellationToken cancellationToken) =>
        Reject<string>();

    public Task UpdateAsync(string id, UpdateBeadRequest request, CancellationToken cancellationToken) => Reject();

    public Task CloseAsync(string id, string? reason, CancellationToken cancellationToken) => Reject();

    public Task DeleteAsync(string id, CancellationToken cancellationToken) => Reject();

    public Task AddCommentAsync(string id, string text, CancellationToken cancellationToken) => Reject();

    public Task AddLabelAsync(string id, string label, CancellationToken cancellationToken) => Reject();

    public Task RemoveLabelAsync(string id, string label, CancellationToken cancellationToken) => Reject();

    public Task AddDependencyAsync(string id, string dependsOnId, DependencyType type, CancellationToken cancellationToken) =>
        Reject();

    public Task RemoveDependencyAsync(string id, string dependsOnId, CancellationToken cancellationToken) => Reject();

    private static int ParseIssueCount(string rawOutput)
    {
        // DoltStore 的 RawOutput 形如 "database=x issues=263"。
        var index = rawOutput.LastIndexOf("issues=", StringComparison.Ordinal);
        return index >= 0
            && int.TryParse(rawOutput[(index + "issues=".Length)..].Trim(), out var count)
                ? count
                : 0;
    }

    private async Task<IReadOnlyList<Bead>> MergeAsync(
        Func<ProjectStore, Task<IReadOnlyList<Bead>>> read,
        CancellationToken cancellationToken)
    {
        var members = await EnsureMembersAsync(cancellationToken).ConfigureAwait(false);
        if (members.Count == 0)
        {
            return [];
        }

        var tasks = members.Select(async member =>
        {
            var beads = await read(member).ConfigureAwait(false);
            return beads.Select(bead => bead with { Project = member.Database }).ToList();
        }).ToList();

        var results = await Task.WhenAll(tasks.Select(TolerantAsync)).ConfigureAwait(false);

        var merged = new List<Bead>();
        var failures = new List<Exception>();
        foreach (var result in results)
        {
            if (result.Error is null)
            {
                merged.AddRange(result.Beads);
            }
            else
            {
                failures.Add(result.Error);
            }
        }

        if (merged.Count == 0 && failures.Count > 0)
        {
            throw failures[0];
        }

        return merged;
    }

    private static async Task<(IReadOnlyList<Bead> Beads, Exception? Error)> TolerantAsync(
        Task<List<Bead>> pending)
    {
        try
        {
            return (await pending.ConfigureAwait(false), null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 单个库不可用时跳过，保证聚合看板仍能显示其余项目。
            return ([], ex);
        }
    }

    /// <summary>按 issue ID 找所属库；同 ID 出现在多个库时取枚举顺序第一个。</summary>
    private async Task<ProjectStore?> FindOwnerAsync(string id, CancellationToken cancellationToken)
    {
        var members = await EnsureMembersAsync(cancellationToken).ConfigureAwait(false);
        foreach (var member in members)
        {
            try
            {
                await member.Store.GetAsync(id, cancellationToken).ConfigureAwait(false);
                return member;
            }
            catch (BdException exception) when (exception.Code == BdErrorCode.NotFound)
            {
                // 该库没有这个 ID，继续试下一个。
            }
        }

        return null;
    }

    private async Task<IReadOnlyList<ProjectStore>> EnsureMembersAsync(CancellationToken cancellationToken)
    {
        if (_members is { } cached)
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_members is { } resolved)
            {
                return resolved;
            }

            var members = await _resolve(cancellationToken).ConfigureAwait(false);
            _members = members;
            return members;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static Task<T> Reject<T>() => Task.FromException<T>(BdException.ReadOnly(ReadOnlyReason));

    private static Task Reject() => Task.FromException(BdException.ReadOnly(ReadOnlyReason));
}
