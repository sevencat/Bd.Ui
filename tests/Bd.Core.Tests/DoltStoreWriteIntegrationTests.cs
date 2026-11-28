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

            // 落库列与 bd 的表结构一致：状态/优先级/描述/label 都能在原始表里读到。
            (await QueryScalarAsync($"SELECT status FROM issues WHERE id = '{id}'")).Should().Be("open");
            (await QueryScalarAsync($"SELECT priority FROM issues WHERE id = '{id}'")).Should().Be(3);
            (await QueryScalarAsync($"SELECT description FROM issues WHERE id = '{id}'"))
                .Should().Be("由 Bd.Ui 集成测试创建，断言后删除");
            (await QueryScalarAsync($"SELECT COUNT(*) FROM labels WHERE issue_id = '{id}' AND label = 'ui-selftest'"))
                .Should().Be(1L);

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

            // 更新事件要能在 bd 的事件流里看到
            (await QueryScalarAsync($"SELECT COUNT(*) FROM events WHERE issue_id = '{id}' AND event_type = 'updated'"))
                .Should().Be(2L);

            // 外部（例如服务器端其它客户端）改行后，UI 重新读取能看到最新值。
            await ExecuteAsync($"UPDATE issues SET title = '外部改标题' WHERE id = '{id}'");
            (await store.GetAsync(id, Timeout)).Title.Should().Be("外部改标题");

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

    private static async Task ExecuteAsync(string sql)
    {
        var factory = new DoltConnectionFactory();
        var settings = new DoltConnectionSettings { Host = Host, Port = Port, User = User, Database = Database };

        await using var connection = factory.CreateConnection(settings);
        await connection.OpenAsync(Timeout);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(Timeout);
    }
}
