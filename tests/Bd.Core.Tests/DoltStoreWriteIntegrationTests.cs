using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Bd.Core.Abstractions;
using Bd.Core.Dolt;
using Bd.Core.Domain;
using FluentAssertions;
using MySqlConnector;
using Xunit;

namespace Bd.Core.Tests;

/// <summary>
/// 写路径对真实服务器的一致性校验（会真的建/改/删任务，默认不跑）。
/// 需要同时设置 BEADS_DOLT_PASSWORD 与 BD_UI_ENABLE_WRITE_TESTS=1。
/// </summary>
public sealed class DoltStoreWriteIntegrationTests
{
    private const string Host = "1.15.245.172";
    private const int Port = 7050;
    private const string User = "nwms";
    private const string Database = "king_bdui";
    private const string EnableVariable = "BD_UI_ENABLE_WRITE_TESTS";
    private const string SelfTitle = "UI 写路径自检（自动清理）";
    private static readonly CancellationToken Timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90)).Token;

    [Fact]
    public async Task Create_update_comment_label_dependency_close_delete_roundtrip()
    {
        var store = CreateStore();
        if (store is null)
        {
            return;
        }

        string? created = null;
        try
        {
            created = await store.CreateAsync(
                new CreateBeadRequest
                {
                    Title = SelfTitle,
                    Description = "由 Bd.Ui 集成测试创建，断言后删除",
                    Priority = 3,
                    Labels = ["ui-selftest"]
                },
                Timeout);
            var id = created;

            id.Should().StartWith("Bd_Ui-");
            id.Should().HaveLength("Bd_Ui-".Length + 3);

            var createdBead = await store.GetAsync(id, Timeout);
            createdBead.Title.Should().Be(SelfTitle);
            createdBead.StatusRaw.Should().Be("open");
            createdBead.PriorityValue.Should().Be(3);

            // bd CLI 能读到 UI 写进去的任务
            var createdJson = ReadBdIssue(await RunBdAsync($"show {id} --json"));
            createdJson.GetProperty("id").GetString().Should().Be(id);
            createdJson.GetProperty("status").GetString().Should().Be("open");
            createdJson.GetProperty("priority").GetInt32().Should().Be(3);
            createdJson.GetProperty("description").GetString().Should().Be("由 Bd.Ui 集成测试创建，断言后删除");
            createdJson.GetProperty("labels").EnumerateArray()
                .Select(static label => label.GetString())
                .Should().Contain("ui-selftest");

            // content_hash 未与 bd 对齐 → 写 NULL；row_lock 必须非 0（bd 的乐观锁列）。
            (await QueryScalarAsync($"SELECT content_hash FROM issues WHERE id = '{id}'")).Should().BeNull();
            Convert.ToInt64(await QueryScalarAsync($"SELECT row_lock FROM issues WHERE id = '{id}'")).Should().BePositive();

            await store.UpdateAsync(
                id,
                new UpdateBeadRequest { Title = $"{SelfTitle}（已更新）", Priority = 1, Notes = "第一条备注" },
                Timeout);
            await store.UpdateAsync(id, new UpdateBeadRequest { Notes = "第二条备注" }, Timeout);

            var updated = await store.GetAsync(id, Timeout);
            updated.Title.Should().Be($"{SelfTitle}（已更新）");
            updated.PriorityValue.Should().Be(1);
            // 与 bd --append-notes 一致：单换行拼接
            (await QueryScalarAsync($"SELECT notes FROM issues WHERE id = '{id}'"))
                .Should().Be("第一条备注\n第二条备注");

            var updatedJson = ReadBdIssue(await RunBdAsync($"show {id} --json"));
            updatedJson.GetProperty("priority").GetInt32().Should().Be(1);
            updatedJson.GetProperty("notes").GetString().Should().Contain("第二条备注");

            // 更新事件要能在 bd 的事件流里看到
            (await QueryScalarAsync($"SELECT COUNT(*) FROM events WHERE issue_id = '{id}' AND event_type = 'updated'"))
                .Should().Be(2L);

            // 反向互操作：bd CLI 能继续改 UI 建的这条任务，UI 也能读到 bd 的改动。
            await RunBdAsync($"update {id} --title \"bd 侧改标题\"");
            (await store.GetAsync(id, Timeout)).Title.Should().Be("bd 侧改标题");
            await store.UpdateAsync(id, new UpdateBeadRequest { Priority = 0 }, Timeout);
            ReadBdIssue(await RunBdAsync($"show {id} --json")).GetProperty("priority").GetInt32().Should().Be(0);

            await store.AddCommentAsync(id, "写路径评论", Timeout);
            await store.AddLabelAsync(id, "ui-extra", Timeout);
            await store.AddDependencyAsync(id, "Bd_Ui-424", DependencyType.Related, Timeout);

            (await store.ListCommentsAsync(id, Timeout)).Should().Contain(comment => comment.Text == "写路径评论");
            (await store.ListDependenciesAsync(id, Timeout))
                .Should().Contain(link => link.TargetId == "Bd_Ui-424" && link.DependencyTypeRaw == "related");
            (await QueryScalarAsync($"SELECT COUNT(*) FROM events WHERE issue_id = '{id}' AND event_type = 'label_added'"))
                .Should().Be(1L);
            (await QueryScalarAsync($"SELECT COUNT(*) FROM events WHERE issue_id = '{id}' AND event_type = 'dependency_added'"))
                .Should().Be(1L);

            await store.RemoveDependencyAsync(id, "Bd_Ui-424", Timeout);
            await store.RemoveLabelAsync(id, "ui-extra", Timeout);
            (await store.ListDependenciesAsync(id, Timeout))
                .Should().NotContain(link => link.TargetId == "Bd_Ui-424");
            (await QueryScalarAsync($"SELECT COUNT(*) FROM labels WHERE issue_id = '{id}' AND label = 'ui-extra'"))
                .Should().Be(0L);

            await store.UpdateAsync(id, new UpdateBeadRequest { Status = "in_progress", Assignee = "sevencat" }, Timeout);
            (await store.GetAsync(id, Timeout)).StatusRaw.Should().Be("in_progress");
            (await QueryScalarAsync($"SELECT assignee FROM issues WHERE id = '{id}'")).Should().Be("sevencat");
            (await QueryScalarAsync($"SELECT COUNT(*) FROM leases WHERE issue_id = '{id}'")).Should().Be(1L);

            await store.CloseAsync(id, "自检完成", Timeout);
            var closed = await store.GetAsync(id, Timeout);
            closed.StatusRaw.Should().Be("closed");
            (await QueryScalarAsync($"SELECT COUNT(*) FROM leases WHERE issue_id = '{id}'")).Should().Be(0L);
            (await QueryScalarAsync($"SELECT close_reason FROM issues WHERE id = '{id}'")).Should().Be("自检完成");

            var closedJson = ReadBdIssue(await RunBdAsync($"show {id} --json"));
            closedJson.GetProperty("status").GetString().Should().Be("closed");
            closedJson.GetProperty("close_reason").GetString().Should().Be("自检完成");

            await store.DeleteAsync(id, Timeout);
            created = null;
            (await QueryScalarAsync($"SELECT COUNT(*) FROM issues WHERE id = '{id}'")).Should().Be(0L);
            (await QueryScalarAsync($"SELECT COUNT(*) FROM comments WHERE issue_id = '{id}'")).Should().Be(0L);
        }
        finally
        {
            if (created is not null)
            {
                try
                {
                    await store.DeleteAsync(created, CancellationToken.None);
                }
                catch
                {
                    // 清理失败不影响断言结果。
                }
            }
        }
    }

    [Fact]
    public async Task Child_issue_uses_parent_counter_and_parent_child_dependency()
    {
        var store = CreateStore();
        if (store is null)
        {
            return;
        }

        string? created = null;
        try
        {
            created = await store.CreateAsync(
                new CreateBeadRequest { Title = "UI 子任务自检（自动清理）", ParentId = "Bd_Ui-424" },
                Timeout);
            var id = created;

            id.Should().StartWith("Bd_Ui-424.");

            var bead = await store.GetAsync(id, Timeout);
            bead.ParentId.Should().Be("Bd_Ui-424");
            (await store.ListDependenciesAsync(id, Timeout))
                .Should().Contain(link => link.TargetId == "Bd_Ui-424" && link.DependencyTypeRaw == "parent-child");

            ReadBdIssue(await RunBdAsync($"show {id} --json")).GetProperty("id").GetString().Should().Be(id);
        }
        finally
        {
            if (created is not null)
            {
                await store.DeleteAsync(created, CancellationToken.None);
            }
        }
    }

    [Fact]
    public async Task Blocking_dependency_flips_is_blocked_like_bd_view()
    {
        var store = CreateStore();
        if (store is null)
        {
            return;
        }

        string? blocker = null;
        string? blocked = null;
        try
        {
            blocker = await store.CreateAsync(new CreateBeadRequest { Title = "UI 阻塞源自检（自动清理）" }, Timeout);
            blocked = await store.CreateAsync(new CreateBeadRequest { Title = "UI 被阻塞项自检（自动清理）" }, Timeout);
            var blockedId = blocked;

            await store.AddDependencyAsync(blockedId, blocker, DependencyType.Blocks, Timeout);

            // 与 bd 的 blocked_issues 视图同口径
            (await QueryScalarAsync($"SELECT is_blocked FROM issues WHERE id = '{blockedId}'")).Should().Be(1);
            (await QueryScalarAsync($"SELECT COUNT(*) FROM blocked_issues WHERE id = '{blockedId}'")).Should().Be(1L);
            (await QueryScalarAsync($"SELECT is_blocked FROM issues WHERE id = '{blocker}'")).Should().Be(0);

            await store.CloseAsync(blocker, "解除阻塞", Timeout);

            (await QueryScalarAsync($"SELECT is_blocked FROM issues WHERE id = '{blockedId}'")).Should().Be(0);
            (await QueryScalarAsync($"SELECT COUNT(*) FROM blocked_issues WHERE id = '{blockedId}'")).Should().Be(0L);
        }
        finally
        {
            foreach (var id in new[] { blocked, blocker })
            {
                if (id is not null)
                {
                    await store.DeleteAsync(id, CancellationToken.None);
                }
            }
        }
    }

    private static DoltStore? CreateStore()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DoltConnectionFactory.PasswordEnvironmentVariable)))
        {
            return null;
        }

        if (!string.Equals(Environment.GetEnvironmentVariable(EnableVariable), "1", StringComparison.Ordinal))
        {
            return null;
        }

        return new DoltStore(
            new DoltConnectionFactory(),
            new DoltConnectionSettings { Host = Host, Port = Port, User = User, Database = Database });
    }

    private static async Task<object?> QueryScalarAsync(string sql)
    {
        var factory = new DoltConnectionFactory();
        var settings = new DoltConnectionSettings { Host = Host, Port = Port, User = User, Database = Database };

        await using var connection = factory.CreateConnection(settings);
        await connection.OpenAsync(Timeout);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync(Timeout);
        return value is DBNull ? null : value;
    }

    /// <summary>bd show --json 的首个 issue 对象；bd 输出带缩进，因此走 JSON 解析而不是字符串匹配。</summary>
    private static JsonElement ReadBdIssue(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return (root.ValueKind == JsonValueKind.Array ? root[0] : root).Clone();
    }

    private static async Task<string> RunBdAsync(string arguments)
    {
        var startInfo = new ProcessStartInfo("bd", arguments)
        {
            WorkingDirectory = FindRepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            // bd 输出是 UTF-8；不显式指定时会按控制台代码页（中文系统为 GBK）解码，导致 JSON 被截断。
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 bd");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        process.ExitCode.Should().Be(0, $"bd {arguments} 应成功，stderr：{stderr}");
        return stdout;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".beads")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Environment.CurrentDirectory;
    }
}
