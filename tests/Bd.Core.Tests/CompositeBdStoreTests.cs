using Bd.Core.Abstractions;
using Bd.Core.BdCli;
using Bd.Core.Dolt;
using Bd.Core.Domain;
using Bd.Core.Filters;
using Bd.Core.Services;
using Bd.Testing.Fakes;
using FluentAssertions;
using Xunit;

namespace Bd.Core.Tests;

/// <summary>聚合多库：合并带上项目标记、按 ID 路由、同 ID 不互相覆盖、写操作拒绝。</summary>
public sealed class CompositeBdStoreTests
{
    private static readonly DoltConnectionSettings Settings = new()
    {
        Host = "example.local",
        User = "tester",
        Database = ProjectInfo.AggregateDatabase
    };

    [Fact]
    public async Task List_merges_projects_and_stamps_project()
    {
        var bdui = Store(("X-1", "任务一"));
        var fh2 = Store(("X-1", "同名 ID"), ("F-1", "任务二"));
        var store = Create(("king_bdui", bdui), ("king_fh2sharp", fh2));

        var beads = await store.ListAsync(new BeadListQuery(), CancellationToken.None);

        beads.Should().HaveCount(3);
        beads.Select(static bead => bead.Project).Should().BeEquivalentTo(["king_bdui", "king_fh2sharp", "king_fh2sharp"]);
        beads.Where(static bead => bead.Id == "X-1").Should().HaveCount(2);
    }

    [Fact]
    public async Task Board_keeps_same_id_from_different_projects()
    {
        var bdui = Store(("X-1", "任务一"));
        var fh2 = Store(("X-1", "同名 ID"));
        var store = Create(("king_bdui", bdui), ("king_fh2sharp", fh2));

        var snapshot = await new BoardService(store).LoadAsync(new BoardFilter(), CancellationToken.None);

        snapshot.Total.Should().Be(2);
        snapshot.Ready.Select(static bead => bead.Project).Should().BeEquivalentTo(["king_bdui", "king_fh2sharp"]);
    }

    [Fact]
    public async Task Get_routes_to_the_owning_database()
    {
        var bdui = Store(("Bd_Ui-1", "本地任务"));
        var fh2 = Store(("FHeroes2Sharp-1", "远端任务"));
        var store = Create(("king_bdui", bdui), ("king_fh2sharp", fh2));

        var bead = await store.GetAsync("FHeroes2Sharp-1", CancellationToken.None);

        bead.Project.Should().Be("king_fh2sharp");
        bead.Title.Should().Be("远端任务");

        var missing = async () => await store.GetAsync("nope-1", CancellationToken.None);
        (await missing.Should().ThrowAsync<BdException>()).Which.Code.Should().Be(BdErrorCode.NotFound);
    }

    [Fact]
    public async Task Dependencies_and_comments_use_the_owning_database()
    {
        var bdui = Store(("Bd_Ui-1", "本地任务"));
        bdui.Beads[0] = bdui.Beads[0] with
        {
            Dependencies = [new DependencyLink { TargetId = "Bd_Ui-2", DependencyTypeRaw = "blocks" }]
        };
        bdui.Comments.Add(new Comment { Id = "c1", IssueId = "Bd_Ui-1", Author = "sevencat", Text = "第一行" });

        var fh2 = Store(("FHeroes2Sharp-1", "远端任务"));
        var store = Create(("king_bdui", bdui), ("king_fh2sharp", fh2));

        var dependencies = await store.ListDependenciesAsync("Bd_Ui-1", CancellationToken.None);
        var comments = await store.ListCommentsAsync("Bd_Ui-1", CancellationToken.None);

        dependencies.Should().ContainSingle().Which.TargetId.Should().Be("Bd_Ui-2");
        comments.Should().ContainSingle().Which.Text.Should().Be("第一行");
        (await store.ListDependenciesAsync("missing-9", CancellationToken.None)).Should().BeEmpty();
        (await store.ListCommentsAsync("missing-9", CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Aggregate_mode_rejects_every_write()
    {
        var bdui = Store(("Bd_Ui-1", "本地任务"));
        var store = Create(("king_bdui", bdui));

        var create = async () => await store.CreateAsync(new CreateBeadRequest { Title = "x" }, CancellationToken.None);
        var update = async () => await store.UpdateAsync("Bd_Ui-1", new UpdateBeadRequest { Title = "y" }, CancellationToken.None);
        var close = async () => await store.CloseAsync("Bd_Ui-1", null, CancellationToken.None);
        var comment = async () => await store.AddCommentAsync("Bd_Ui-1", "note", CancellationToken.None);
        var label = async () => await store.AddLabelAsync("Bd_Ui-1", "ui", CancellationToken.None);
        var dependency = async () => await store.AddDependencyAsync("Bd_Ui-1", "Bd_Ui-2", DependencyType.Blocks, CancellationToken.None);

        foreach (var action in new Func<Task>[] { create, update, close, comment, label, dependency })
        {
            (await action.Should().ThrowAsync<BdException>()).Which.Code.Should().Be(BdErrorCode.ReadOnly);
        }

        bdui.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task One_broken_project_does_not_break_the_merged_board()
    {
        var healthy = Store(("Bd_Ui-1", "正常任务"));
        var broken = new ThrowingStore();
        var store = Create(("broken_db", broken), ("king_bdui", healthy));

        var beads = await store.ListAsync(new BeadListQuery(), CancellationToken.None);

        beads.Should().ContainSingle().Which.Project.Should().Be("king_bdui");
    }

    [Fact]
    public async Task Every_project_failing_surfaces_the_first_error()
    {
        var store = Create(("broken_db", new ThrowingStore()));

        var list = async () => await store.ListAsync(new BeadListQuery(), CancellationToken.None);

        await list.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void Aggregate_entry_is_labelled_and_kept_first()
    {
        ProjectInfo.Aggregate(269).Label.Should().Be("全部项目 (269)");
        ProjectInfo.Aggregate(269).Database.Should().Be(CompositeBdStore.AggregateDatabase);
        new ProjectInfo("king_bdui", "Bd_Ui", 6, "1.3.4").IsAggregate.Should().BeFalse();
    }

    [Fact]
    public async Task Live_aggregate_lists_every_project_on_the_server()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DoltConnectionFactory.PasswordEnvironmentVariable)))
        {
            return;
        }

        var factory = new DoltConnectionFactory();
        var settings = new DoltConnectionSettings
        {
            Host = "1.15.245.172",
            Port = 7050,
            User = "nwms",
            Database = ProjectInfo.AggregateDatabase
        };
        var store = CompositeBdStore.ForServer(factory, settings, new ProjectCatalog(factory));

        var beads = await store.ListAsync(new BeadListQuery(), CancellationToken.None);

        beads.Select(static bead => bead.Project).Distinct()
            .Should().Contain(["king_bdui", "king_fh2sharp"]);
        beads.Count(static bead => bead.Project == "king_fh2sharp").Should().BeGreaterThan(200);
        beads.Where(static bead => bead.Project == "king_bdui").Should().NotBeEmpty();
        beads.Should().OnlyContain(static bead => !string.IsNullOrWhiteSpace(bead.Id) && !string.IsNullOrWhiteSpace(bead.Project));

        var bead = await store.GetAsync("Bd_Ui-424", CancellationToken.None);
        bead.Project.Should().Be("king_bdui");

        var write = async () => await store.CreateAsync(new CreateBeadRequest { Title = "不该写入" }, CancellationToken.None);
        (await write.Should().ThrowAsync<BdException>()).Which.UserMessage.Should().Contain("聚合模式只读");
    }

    private static InMemoryBdStore Store(params (string Id, string Title)[] beads)
    {
        var store = new InMemoryBdStore();
        store.Beads.AddRange(beads.Select(bead => new Bead
        {
            Id = bead.Id,
            Title = bead.Title,
            StatusRaw = "open",
            CreatedBy = "sevencat",
            UpdatedAt = new DateTimeOffset(2026, 11, 27, 10, 0, 0, TimeSpan.Zero)
        }));
        return store;
    }

    private static CompositeBdStore Create(params (string Database, IBdStore Store)[] members)
    {
        IReadOnlyList<ProjectStore> resolved =
            [.. members.Select(member => new ProjectStore(member.Database, member.Store))];
        return new CompositeBdStore(Settings, _ => Task.FromResult(resolved));
    }

    private sealed class ThrowingStore : IBdStore
    {
        public string BackendName => "throwing";

        public Task<IReadOnlyList<Bead>> ListAsync(BeadListQuery query, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("库不可用");

        public Task<IReadOnlyList<Bead>> ReadyAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("库不可用");

        public Task<IReadOnlyList<Bead>> BlockedAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("库不可用");

        public Task<Bead> GetAsync(string id, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("库不可用");

        public Task<IReadOnlyList<DependencyLink>> ListDependenciesAsync(string id, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("库不可用");

        public Task<IReadOnlyList<Comment>> ListCommentsAsync(string id, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("库不可用");

        public Task<string> GetVersionAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("库不可用");

        public Task<DoctorReport> RunDoctorAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("库不可用");

        public Task<string> CreateAsync(CreateBeadRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("库不可用");

        public Task UpdateAsync(string id, UpdateBeadRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("库不可用");

        public Task CloseAsync(string id, string? reason, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("库不可用");

        public Task DeleteAsync(string id, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("库不可用");

        public Task AddCommentAsync(string id, string text, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("库不可用");

        public Task AddLabelAsync(string id, string label, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("库不可用");

        public Task RemoveLabelAsync(string id, string label, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("库不可用");

        public Task AddDependencyAsync(string id, string dependsOnId, DependencyType type, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("库不可用");

        public Task RemoveDependencyAsync(string id, string dependsOnId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("库不可用");
    }
}
