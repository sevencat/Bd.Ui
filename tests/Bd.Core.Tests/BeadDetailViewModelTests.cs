using Bd.Core.Abstractions;
using Bd.Core.Config;
using Bd.Core.Domain;
using Bd.Core.Services;
using Bd.Core.ViewModels;
using Bd.Testing.Fakes;
using FluentAssertions;
using Xunit;

namespace Bd.Core.Tests;

public sealed class BeadDetailViewModelTests
{
    [Fact]
    public async Task 加载详情填充字段评论依赖与子任务()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(Bead("b1", "父任务", "open"));
        store.Beads.Add(Bead("b2", "子任务", "open", parentId: "b1"));
        store.Beads.Add(Bead("dep1", "前置依赖", "open"));
        store.Comments.Add(new Comment
        {
            Id = "c1",
            IssueId = "b1",
            Author = "alice",
            Text = "第一条评论",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await store.AddDependencyAsync("b1", "dep1", DependencyType.Blocks, CancellationToken.None);

        var (vm, _) = CreateDetail(store);
        await vm.LoadAsync("b1");

        vm.IsOpen.Should().BeTrue();
        vm.Title.Should().Be("父任务");
        vm.Body.Should().Be("任务描述");
        vm.StatusText.Should().Be("Open");
        vm.Comments.Should().ContainSingle(row => row.Text == "第一条评论");
        vm.Dependencies.Should().ContainSingle(row => row.TargetId == "dep1" && row.IsOpenBlock);
        vm.Children.Should().ContainSingle(row => row.Id == "b2");
        vm.HasChildren.Should().BeTrue();
        vm.ProgressText.Should().Be("0 / 1");
        vm.ProgressRatio.Should().Be(0);
        vm.MetaText.Should().Contain("b1");
    }

    [Fact]
    public async Task 打开新任务前先清空旧内容并进入加载态()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(Bead("b1", "父任务", "open"));
        store.Beads.Add(Bead("b2", "另一个任务", "open"));

        var (vm, _) = CreateDetail(store);
        await vm.LoadAsync("b1");
        vm.Title.Should().Be("父任务");

        vm.BeginLoading("b2");

        vm.IsOpen.Should().BeTrue("加载期面板要保持可见");
        vm.IsLoading.Should().BeTrue();
        vm.Id.Should().Be("b2");
        vm.Title.Should().BeEmpty("旧任务内容要先清掉");
        vm.Body.Should().BeEmpty();
        vm.Comments.Should().BeEmpty();

        await vm.LoadAsync("b2");
        vm.IsLoading.Should().BeFalse();
        vm.Title.Should().Be("另一个任务");
    }

    [Fact]
    public async Task 依赖按类型分成依赖组与关联组()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(Bead("b1", "任务", "open"));
        store.Beads.Add(Bead("dep1", "阻塞项", "open"));
        store.Beads.Add(Bead("rel1", "关联项", "in_progress"));
        await store.AddDependencyAsync("b1", "dep1", DependencyType.Blocks, CancellationToken.None);
        await store.AddDependencyAsync("b1", "rel1", DependencyType.Related, CancellationToken.None);

        var (vm, _) = CreateDetail(store);
        await vm.LoadAsync("b1");

        vm.HasDependsOn.Should().BeTrue();
        vm.HasRelated.Should().BeTrue();
        vm.HasDependencies.Should().BeTrue();
        vm.DependsOn.Should().ContainSingle(row => row.TargetId == "dep1" && !row.IsClosed);
        vm.Related.Should().ContainSingle(row => row.TargetId == "rel1" && row.IsInProgress);
        vm.Dependencies.Should().HaveCount(2);
        vm.DependsOn[0].Heading.Should().Be("dep1: 阻塞项");
    }

    [Fact]
    public async Task 详情展示设计验收与变更历史()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(Bead("b1", "任务", "closed") with
        {
            Design = "先写接口",
            AcceptanceCriteria = "全部测试通过",
            CloseReason = "已交付",
            Assignee = "bob"
        });
        store.Events.Add(new BeadEvent
        {
            IssueId = "b1",
            EventTypeRaw = "status_changed",
            Actor = "bob",
            OldValue = "open",
            NewValue = "closed",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var (vm, _) = CreateDetail(store);
        await vm.LoadAsync("b1");

        vm.DesignText.Should().Be("先写接口");
        vm.HasDesign.Should().BeTrue();
        vm.AcceptanceText.Should().Be("全部测试通过");
        vm.HasAcceptance.Should().BeTrue();
        vm.CloseReasonText.Should().Be("已交付");
        vm.HasCloseReason.Should().BeTrue();
        vm.AssigneeText.Should().Be("bob");
        vm.Events.Should().ContainSingle(row =>
            row.TypeText == "Status" && row.DetailText == "open → closed" && row.ActorText == "bob");
        vm.HasEvents.Should().BeTrue();
    }

    [Fact]
    public async Task 变更历史单边取值不拼空箭头且关闭原因不重复()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(Bead("b1", "任务", "closed") with { CloseReason = "三个子项目全部完成" });
        store.Events.Add(new BeadEvent
        {
            IssueId = "b1",
            EventTypeRaw = "closed",
            Actor = "bob",
            OldValue = string.Empty,
            NewValue = "三个子项目全部完成",
            CreatedAt = DateTimeOffset.UtcNow
        });
        store.Events.Add(new BeadEvent
        {
            IssueId = "b1",
            EventTypeRaw = "updated",
            Actor = "bob",
            NewValue = "改了标题",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var (vm, _) = CreateDetail(store);
        await vm.LoadAsync("b1");

        vm.Events.Should().HaveCount(2);
        // 关闭事件的明细与关闭原因一致，行内不再重复展示。
        vm.Events.Should().Contain(row => row.TypeText == "Closed" && !row.HasDetail);
        // 只有新值时不出现 "→ xxx" 这种残缺箭头。
        vm.Events.Should().Contain(row => row.DetailText == "改了标题");
    }

    [Fact]
    public async Task 变更历史快照只显示差异字段()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(Bead("b1", "任务", "in_progress"));
        store.Events.Add(new BeadEvent
        {
            IssueId = "b1",
            EventTypeRaw = "updated",
            Actor = "bob",
            OldValue = """
                       {"id":"b1","title":"任务","status":"in_progress","issue_type":"task","created_at":"2026-11-24T23:18:45Z"}
                       """,
            NewValue = """{"issue_type":"epic","status":"in_progress"}""",
            CreatedAt = DateTimeOffset.UtcNow
        });
        // 只有一侧是快照（整行 JSON）时不铺原文，避免一屏弹出一大段 JSON。
        store.Events.Add(new BeadEvent
        {
            IssueId = "b1",
            EventTypeRaw = "claimed",
            Actor = "bob",
            OldValue = """{"id":"b1","title":"任务"}""",
            NewValue = string.Empty,
            CreatedAt = DateTimeOffset.UtcNow
        });

        var (vm, _) = CreateDetail(store);
        await vm.LoadAsync("b1");

        var updated = vm.Events.Single(row => row.TypeText == "Updated");
        updated.HasDetail.Should().BeFalse();
        updated.HasChanges.Should().BeTrue();
        updated.Changes.Should().HaveCount(1);
        updated.Changes[0].Field.Should().Be("类型");
        updated.Changes[0].OldText.Should().Be("task");
        updated.Changes[0].NewText.Should().Be("epic");
        updated.Changes[0].HasArrow.Should().BeTrue();

        var claimed = vm.Events.Single(row => row.TypeText == "claimed");
        claimed.HasDetail.Should().BeFalse();
        claimed.HasChanges.Should().BeFalse();
    }

    [Fact]
    public async Task 父任务跳转触发导航事件()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(Bead("b1", "父任务", "open"));
        store.Beads.Add(Bead("b2", "子任务", "open", parentId: "b1"));

        var (vm, _) = CreateDetail(store);
        await vm.LoadAsync("b2");

        string? navigated = null;
        vm.NavigateRequested += (_, id) => navigated = id;

        vm.HasParent.Should().BeTrue();
        vm.OpenParentCommand.Execute(null);

        navigated.Should().Be("b1");
    }

    [Fact]
    public async Task 加载失败关闭抽屉并提示错误()
    {
        var store = new InMemoryBdStore();
        var (vm, toast) = CreateDetail(store);

        await vm.LoadAsync("nope");

        vm.IsOpen.Should().BeFalse();
        toast.Items.Should().Contain(item => item.Level == ToastLevel.Error && item.Text.Contains("nope"));
    }

    [Fact]
    public async Task 添加评论忽略空文本并刷新列表()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(Bead("b1", "任务", "open"));
        var (vm, _) = CreateDetail(store);
        await vm.LoadAsync("b1");

        vm.CommentText = "   ";
        await vm.AddCommentAsync();
        store.Comments.Should().BeEmpty();

        vm.CommentText = "新评论";
        await vm.AddCommentAsync();

        vm.CommentText.Should().BeEmpty();
        vm.Comments.Should().ContainSingle(row => row.Text == "新评论");
    }

    [Fact]
    public async Task 归档关闭抽屉并提示成功()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(Bead("b1", "任务", "open"));
        var (vm, toast) = CreateDetail(store);
        await vm.LoadAsync("b1");

        var closed = false;
        vm.Closed += (_, _) => closed = true;

        await vm.ArchiveAsync();

        closed.Should().BeTrue();
        vm.IsOpen.Should().BeFalse();
        toast.Items.Should().Contain(item => item.Level == ToastLevel.Success && item.Text.Contains("已归档"));
    }

    [Fact]
    public async Task 删除移除任务并关闭抽屉()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(Bead("b1", "任务", "open"));
        var (vm, toast) = CreateDetail(store);
        await vm.LoadAsync("b1");

        await vm.DeleteAsync();

        store.Beads.Should().NotContain(bead => bead.Id == "b1");
        vm.IsOpen.Should().BeFalse();
        toast.Items.Should().Contain(item => item.Level == ToastLevel.Warning && item.Text.Contains("已删除"));
    }

    [Fact]
    public async Task 已归档任务照常只读展示()
    {
        var store = new InMemoryBdStore();
        var archived = Bead("b1", "归档任务", "closed");
        store.Beads.Add(archived with { Labels = [BdLabels.Archived] });

        var (vm, _) = CreateDetail(store);
        await vm.LoadAsync("b1");

        vm.IsArchived.Should().BeTrue();
        vm.StatusText.Should().Be("Done");
        vm.AssigneeText.Should().Be("未指派");
    }

    private static (BeadDetailViewModel Vm, ToastService Toast) CreateDetail(IBdStore store, ToastService? toast = null)
    {
        toast ??= new ToastService();
        var attribution = new AttributionService(AppConfig.Default);
        var vm = new BeadDetailViewModel(
            new BeadDetailService(store, attribution),
            new BeadWriteService(store),
            new CommentService(store),
            new ArchiveService(store),
            toast);
        return (vm, toast);
    }

    private static Bead Bead(string id, string title, string status, string? parentId = null) => new()
    {
        Id = id,
        Title = title,
        Description = "任务描述",
        StatusRaw = status,
        TypeRaw = "task",
        PriorityValue = 2,
        Owner = "alice",
        ParentId = parentId,
        CreatedBy = "alice",
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
    };
}
