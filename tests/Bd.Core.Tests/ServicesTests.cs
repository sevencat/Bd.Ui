using Bd.Core.Abstractions;
using Bd.Core.Config;
using Bd.Core.Domain;
using Bd.Core.Services;
using Bd.Testing.Fakes;
using FluentAssertions;
using Xunit;

namespace Bd.Core.Tests;

public sealed class ServicesTests
{
    private static Bead Make(
        string id,
        string status = "open",
        int priority = 2,
        string type = "task",
        string? parent = null,
        string[]? labels = null,
        params (string Id, string Status)[] dependencies)
    {
        var links = dependencies
            .Select(dep => new DependencyLink
            {
                TargetId = dep.Id,
                TargetStatusRaw = dep.Status,
                DependencyTypeRaw = "blocks"
            })
            .ToList();

        return new Bead
        {
            Id = id,
            Title = $"任务 {id}",
            StatusRaw = status,
            PriorityValue = priority,
            TypeRaw = type,
            ParentId = parent,
            Labels = labels ?? [],
            CreatedBy = "sevencat",
            UpdatedAt = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero),
            Dependencies = links
        };
    }

    [Fact]
    public async Task Board_classifies_columns_with_blocked_precedence()
    {
        var store = new InMemoryBdStore();
        store.Beads.AddRange(
        [
            Make("b-backlog", "deferred"),
            Make("b-ready", "open"),
            Make("b-blocked", "open", dependencies: [("b-ready", "open")]),
            Make("b-wip", "in_progress"),
            Make("b-done", "closed"),
            Make("b-status-blocked", "blocked")
        ]);
        var service = new BoardService(store);

        var snapshot = await service.LoadAsync(CancellationToken.None);

        snapshot.Backlog.Should().ContainSingle().Which.Id.Should().Be("b-backlog");
        snapshot.Ready.Should().ContainSingle().Which.Id.Should().Be("b-ready");
        snapshot.Blocked.Select(static bead => bead.Id).Should().BeEquivalentTo(["b-blocked", "b-status-blocked"]);
        snapshot.InProgress.Should().ContainSingle().Which.Id.Should().Be("b-wip");
        // 看板只加载未关闭任务，已关闭的不再进快照。
        snapshot.Done.Should().BeEmpty();
        snapshot.Total.Should().Be(5);
    }

    [Fact]
    public async Task Board_can_include_closed_when_user_asks_for_it()
    {
        var store = new InMemoryBdStore();
        store.Beads.AddRange([Make("b-ready", "open"), Make("b-done", "closed")]);

        var snapshot = await new BoardService(store).LoadAsync(includeClosed: true, CancellationToken.None);

        snapshot.Done.Should().ContainSingle().Which.Id.Should().Be("b-done");
        snapshot.Total.Should().Be(2);
    }

    [Fact]
    public async Task Board_orders_by_priority_then_updated_at()
    {
        var store = new InMemoryBdStore();
        store.Beads.AddRange(
        [
            Make("low", priority: 3),
            Make("high", priority: 0),
            Make("mid", priority: 2)
        ]);

        var snapshot = await new BoardService(store).LoadAsync(CancellationToken.None);

        snapshot.Ready.Select(static bead => bead.Id).Should().Equal("high", "mid", "low");
    }

    [Fact]
    public async Task Board_hides_archived_beads()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(Make("arch", labels: [BdLabels.Archived]));
        store.Beads.Add(Make("live"));

        var snapshot = await new BoardService(store).LoadAsync(CancellationToken.None);

        snapshot.Ready.Select(static bead => bead.Id).Should().Equal("live");
        snapshot.Total.Should().Be(1);
    }

    [Fact]
    public async Task Detail_aggregates_children_and_progress()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(Make("epic-1", type: "epic"));
        store.Beads.Add(Make("child-1", parent: "epic-1", status: "closed"));
        store.Beads.Add(Make("child-2", parent: "epic-1"));
        var attribution = new AttributionService(new AppConfig { HumanActor = "sevencat" });
        var service = new BeadDetailService(store, attribution);

        var detail = await service.LoadAsync("epic-1", CancellationToken.None);

        detail.Bead.Id.Should().Be("epic-1");
        detail.Children.Should().HaveCount(2);
        detail.Progress.Closed.Should().Be(1);
        detail.Progress.Total.Should().Be(2);
        detail.Progress.Ratio.Should().Be(0.5);
        detail.Attribution.Kind.Should().Be(AttributionKind.Human);
    }

    [Fact]
    public async Task Detail_throws_not_found_for_unknown_id()
    {
        var store = new InMemoryBdStore();
        var service = new BeadDetailService(store, new AttributionService(AppConfig.Default));

        var act = () => service.LoadAsync("missing", CancellationToken.None);

        (await act.Should().ThrowAsync<BdException>()).Which.Code.Should().Be(BdErrorCode.NotFound);
    }

    [Fact]
    public void Build_patch_only_keeps_dirty_fields()
    {
        var current = Make("p-1", priority: 2);

        var patch = BeadWriteService.BuildPatch(current, new UpdateBeadRequest
        {
            Title = current.Title,
            Priority = 2,
            Status = "in_progress"
        });

        patch.Title.Should().BeNull();
        patch.Priority.Should().BeNull();
        patch.Status.Should().Be("in_progress");
        patch.HasChanges.Should().BeTrue();
    }

    [Fact]
    public async Task Writing_services_raise_events_and_call_store()
    {
        var store = new InMemoryBdStore();
        var writes = new BeadWriteService(store);
        var changed = new List<string>();
        writes.BeadChanged += (_, id) => changed.Add(id);

        var id = await writes.CreateAsync(new CreateBeadRequest { Title = "新任务", Type = "epic" }, CancellationToken.None);
        await writes.UpdateAsync(id, new UpdateBeadRequest { Status = "in_progress" }, CancellationToken.None);
        await writes.CloseAsync(id, "done", CancellationToken.None);

        changed.Should().Equal(id, id, id);
        store.Calls.Should().Contain("create").And.Contain("update").And.Contain("close");
        store.Beads.Single().Status.Should().Be(BeadStatus.Closed);
    }

    [Fact]
    public async Task Archive_adds_label_and_unarchive_reopens()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(Make("a-1"));
        var service = new ArchiveService(store);

        await service.ArchiveAsync("a-1", "cleanup", CancellationToken.None);
        var archived = store.Beads.Single();
        archived.IsArchived.Should().BeTrue();
        archived.IsClosed.Should().BeTrue();

        await service.UnarchiveAsync("a-1", CancellationToken.None);
        var restored = store.Beads.Single();
        restored.IsArchived.Should().BeFalse();
        restored.Status.Should().Be(BeadStatus.Open);
    }

    [Fact]
    public async Task Comments_reject_blank_text()
    {
        var service = new CommentService(new InMemoryBdStore());

        var act = () => service.AddAsync("x", "  ", CancellationToken.None);

        (await act.Should().ThrowAsync<BdException>()).Which.Code.Should().Be(BdErrorCode.InvalidArgument);
    }

    [Fact]
    public async Task Epic_progress_counts_closed_children()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(Make("epic-e", type: "epic"));
        store.Beads.Add(Make("e-1", parent: "epic-e", status: "closed"));
        store.Beads.Add(Make("e-2", parent: "epic-e"));
        store.Beads.Add(Make("epic-empty", type: "epic"));
        var service = new EpicProgressService(store);

        var items = await service.LoadAsync(includeEmpty: false, CancellationToken.None);

        var item = items.Should().ContainSingle().Subject;
        item.Epic.Id.Should().Be("epic-e");
        item.Progress.Closed.Should().Be(1);
        item.Progress.Total.Should().Be(2);
        item.Progress.Ratio.Should().Be(0.5);
    }

    [Theory]
    [InlineData("sevencat", AttributionKind.Human)]
    [InlineData("alice", AttributionKind.Human)]
    [InlineData("claude-agent", AttributionKind.Agent)]
    [InlineData(null, AttributionKind.Unknown)]
    public void Attribution_uses_config_allowlist(string? actor, AttributionKind expected)
    {
        var service = new AttributionService(new AppConfig
        {
            HumanActor = "sevencat",
            HumanAllowlist = ["Alice"]
        });

        service.Resolve(actor).Kind.Should().Be(expected);
    }

    [Fact]
    public async Task Refresh_coordinator_refreshes_on_demand_only()
    {
        var count = 0;
        await using var coordinator = new RefreshCoordinator(
            _ =>
            {
                count++;
                return Task.CompletedTask;
            },
            new FakeClock());

        count.Should().Be(0, "协调器不做定时轮询，构造后不会自己刷新");

        await coordinator.RefreshNowAsync(CancellationToken.None);
        await coordinator.RefreshNowAsync(CancellationToken.None);

        count.Should().Be(2);
        coordinator.RefreshCount.Should().Be(2);
        coordinator.LastRefreshedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Refresh_failure_propagates_to_caller()
    {
        await using var coordinator = new RefreshCoordinator(
            _ => throw new InvalidOperationException("boom"),
            new FakeClock());

        var act = () => coordinator.RefreshNowAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = new(2026, 11, 25, 0, 0, 0, TimeSpan.Zero);

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
}
