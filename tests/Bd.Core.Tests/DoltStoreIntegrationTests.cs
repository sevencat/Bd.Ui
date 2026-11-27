using Bd.Core.Abstractions;
using Bd.Core.Dolt;
using Bd.Core.Domain;
using FluentAssertions;
using Xunit;

namespace Bd.Core.Tests;

/// <summary>
/// 对真实 Dolt 服务器（1.15.245.172:7050）的只读校验。
/// 未设置 BEADS_DOLT_PASSWORD 时直接跳过，保证无内网环境仍可跑全量测试。
/// </summary>
public sealed class DoltStoreIntegrationTests
{
    private const string Host = "1.15.245.172";
    private const int Port = 7050;
    private const string User = "nwms";

    [Fact]
    public async Task Reads_issue_with_parent_dependencies_and_counts()
    {
        var store = CreateStore("king_bdui");
        if (store is null)
        {
            return;
        }

        var bead = await store.GetAsync("Bd_Ui-424.2", CancellationToken.None);

        bead.Id.Should().Be("Bd_Ui-424.2");
        bead.Title.Should().Contain("DoltStore");
        bead.ParentId.Should().Be("Bd_Ui-424");
        bead.Dependencies.Should().Contain(link => link.TargetId == "Bd_Ui-424" && link.DependencyTypeRaw == "parent-child");
        bead.CreatedAt.Should().NotBeNull();
        bead.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task List_and_detail_agree_on_one_issue()
    {
        var store = CreateStore("king_bdui");
        if (store is null)
        {
            return;
        }

        var listed = await store.ListAsync(new BeadListQuery { Search = "DoltStore" }, CancellationToken.None);
        var match = listed.First(bead => bead.Id == "Bd_Ui-424.2");
        var detail = await store.GetAsync(match.Id, CancellationToken.None);

        match.Title.Should().Be(detail.Title);
        match.StatusRaw.Should().Be(detail.StatusRaw);
        match.ParentId.Should().Be(detail.ParentId);
        match.CreatedAt.Should().Be(detail.CreatedAt);
    }

    [Fact]
    public async Task Dependencies_are_readable()
    {
        var store = CreateStore("king_bdui");
        if (store is null)
        {
            return;
        }

        var dependencies = await store.ListDependenciesAsync("Bd_Ui-424.2", CancellationToken.None);

        dependencies.Should().Contain(link => link.TargetId == "Bd_Ui-424.1" && link.DependencyTypeRaw == "blocks");
        dependencies.Should().OnlyContain(link => !string.IsNullOrEmpty(link.TargetId));
    }

    [Fact]
    public async Task Comments_are_ordered_and_scoped_to_the_issue()
    {
        var store = CreateStore("king_fh2sharp");
        if (store is null)
        {
            return;
        }

        var comments = await store.ListCommentsAsync("FHeroes2Sharp-4f2.1.3", CancellationToken.None);

        comments.Should().NotBeEmpty();
        comments.Should().OnlyContain(comment => comment.IssueId == "FHeroes2Sharp-4f2.1.3");
        comments.Should().BeInAscendingOrder(comment => comment.CreatedAt);
    }

    [Fact]
    public async Task Ready_and_blocked_views_are_queryable()
    {
        var store = CreateStore("king_bdui");
        if (store is null)
        {
            return;
        }

        var ready = await store.ReadyAsync(CancellationToken.None);
        var blocked = await store.BlockedAsync(CancellationToken.None);

        // 真实库数据会随开发推进变化（可能没有 ready/blocked 项），只校验视图口径本身。
        ready.Should().NotContain(bead => bead.IsClosed);
        blocked.Should().NotContain(bead => bead.Status == BeadStatus.Closed || bead.Status == BeadStatus.Pinned);
        ready.Should().OnlyHaveUniqueItems(bead => bead.Id);
        blocked.Should().OnlyHaveUniqueItems(bead => bead.Id);
    }

    [Fact]
    public async Task Doctor_reports_version_and_schema()
    {
        var store = CreateStore("king_bdui");
        if (store is null)
        {
            return;
        }

        var version = await store.GetVersionAsync(CancellationToken.None);
        var report = await store.RunDoctorAsync(CancellationToken.None);

        version.Should().NotBeNullOrWhiteSpace();
        report.Ok.Should().BeTrue(report.Summary);
        report.BdVersion.Should().Be(version);
        report.SchemaVersion.Should().BeGreaterThan(0);
        report.RepoPath.Should().Be($"dolt://{Host}:{Port}/king_bdui");
    }

    [Fact]
    public async Task Other_project_database_is_queryable()
    {
        var store = CreateStore("king_fh2sharp");
        if (store is null)
        {
            return;
        }

        var beads = await store.ListAsync(new BeadListQuery { Limit = 5 }, CancellationToken.None);

        beads.Should().NotBeEmpty();
        beads.Should().OnlyContain(bead => bead.Id.StartsWith("FHeroes2Sharp-", StringComparison.Ordinal));
    }

    private static DoltStore? CreateStore(string database)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DoltConnectionFactory.PasswordEnvironmentVariable)))
        {
            return null;
        }

        return new DoltStore(
            new DoltConnectionFactory(),
            new DoltConnectionSettings { Host = Host, Port = Port, User = User, Database = database });
    }
}
