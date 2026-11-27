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
        vm.SelectedStatus.Should().Be("open");
        vm.Comments.Should().ContainSingle(row => row.Text == "第一条评论");
        vm.Dependencies.Should().ContainSingle(row => row.TargetId == "dep1" && row.IsOpenBlock);
        vm.Children.Should().ContainSingle(row => row.Id == "b2");
        vm.HasChildren.Should().BeTrue();
        vm.ProgressText.Should().Be("0 / 1");
        vm.ProgressRatio.Should().Be(0);
        vm.MetaText.Should().Contain("b1");
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
    public async Task 保存状态与指派并通知变更()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(Bead("b1", "任务", "open"));
        var (vm, toast) = CreateDetail(store);
        await vm.LoadAsync("b1");

        var changed = false;
        vm.Changed += (_, _) => changed = true;

        vm.SelectedStatus = "in_progress";
        vm.AssigneeInput = "bob";
        await vm.SaveAsync();

        var saved = store.Beads.Single(bead => bead.Id == "b1");
        saved.StatusRaw.Should().Be("in_progress");
        saved.Owner.Should().Be("bob");
        changed.Should().BeTrue();
        vm.IsBusy.Should().BeFalse();
        toast.Items.Should().Contain(item => item.Level == ToastLevel.Success && item.Text.Contains("已更新"));
    }

    [Fact]
    public async Task 保存失败保留编辑内容并提示错误()
    {
        var inner = new InMemoryBdStore();
        inner.Beads.Add(Bead("b1", "任务", "open"));
        var store = new FaultyBdStore(inner);
        var (vm, toast) = CreateDetail(store);
        await vm.LoadAsync("b1");

        store.WriteError = new BdException(BdErrorCode.Unknown, "写入被拒绝");
        vm.SelectedStatus = "blocked";
        await vm.SaveAsync();

        vm.SelectedStatus.Should().Be("blocked");
        inner.Beads.Single(bead => bead.Id == "b1").StatusRaw.Should().Be("open");
        toast.Items.Should().Contain(item => item.Level == ToastLevel.Error);
        vm.IsBusy.Should().BeFalse();
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
    public async Task 已归档任务不可编辑()
    {
        var store = new InMemoryBdStore();
        var archived = Bead("b1", "归档任务", "closed");
        store.Beads.Add(archived with { Labels = [BdLabels.Archived] });

        var (vm, _) = CreateDetail(store);
        await vm.LoadAsync("b1");

        vm.IsArchived.Should().BeTrue();
        vm.CanEdit.Should().BeFalse();
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
