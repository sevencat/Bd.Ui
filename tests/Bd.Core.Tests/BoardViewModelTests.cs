using Bd.Core.Abstractions;
using Bd.Core.BdCli;
using Bd.Core.Config;
using Bd.Core.Domain;
using Bd.Core.Services;
using Bd.Core.ViewModels;
using Bd.Testing.Fakes;
using FluentAssertions;
using Xunit;

namespace Bd.Core.Tests;

public sealed class BoardViewModelTests
{
    [Fact]
    public async Task 加载后按状态分列并统计()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(Bead("b1", "待办", "open"));
        store.Beads.Add(Bead("b2", "进行中", "in_progress"));
        store.Beads.Add(Bead("b3", "已完成", "closed"));
        store.Beads.Add(Bead("b4", "延期", "deferred"));

        var (vm, _) = CreateBoard(store);
        await vm.RefreshAsync();

        vm.TotalCount.Should().Be(4);
        Column(vm, BoardColumnKind.Ready).Cards.Should().ContainSingle(card => card.Id == "b1");
        Column(vm, BoardColumnKind.InProgress).Cards.Should().ContainSingle(card => card.Id == "b2");
        Column(vm, BoardColumnKind.Done).Cards.Should().ContainSingle(card => card.Id == "b3");
        Column(vm, BoardColumnKind.Backlog).Cards.Should().ContainSingle(card => card.Id == "b4");
        vm.ErrorMessage.Should().BeNull();
        vm.LastLoadedText.Should().NotBe("尚未加载");
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task 搜索筛选只保留命中卡片()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(Bead("b1", "登录页打磨", "open"));
        store.Beads.Add(Bead("b2", "依赖图渲染", "open"));

        var (vm, _) = CreateBoard(store);
        await vm.RefreshAsync();

        vm.Filter.SearchText = "依赖图";
        await vm.RefreshCoreAsync(CancellationToken.None, silent: true);

        vm.TotalCount.Should().Be(1);
        vm.Columns.SelectMany(column => column.Cards)
            .Should().ContainSingle(card => card.Id == "b2");
    }

    [Fact]
    public async Task 移列成功写入并刷新到目标列()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(Bead("b1", "待办", "open"));

        var (vm, toast) = CreateBoard(store);
        await vm.RefreshAsync();

        var card = FindCard(vm, "b1");
        card.Should().NotBeNull();

        await vm.MoveAsync(card, BoardColumnKind.InProgress);

        store.Beads.Single(bead => bead.Id == "b1").StatusRaw.Should().Be("in_progress");
        Column(vm, BoardColumnKind.InProgress).Cards.Should().ContainSingle(item => item.Id == "b1");
        Column(vm, BoardColumnKind.Ready).Cards.Should().BeEmpty();
        toast.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task 移列失败回滚到原列并提示错误()
    {
        var inner = new InMemoryBdStore();
        inner.Beads.Add(Bead("b1", "待办", "open"));
        var store = new FaultyBdStore(inner);
        var toast = new ToastService();
        var (vm, _) = CreateBoard(store, toast);

        await vm.RefreshAsync();
        var card = FindCard(vm, "b1");

        store.WriteError = new BdException(BdErrorCode.Unknown, "写入被拒绝");
        await vm.MoveAsync(card, BoardColumnKind.InProgress);

        store.WriteCalls.Should().Be(1);
        inner.Beads.Single(bead => bead.Id == "b1").StatusRaw.Should().Be("open");
        Column(vm, BoardColumnKind.Ready).Cards.Should().ContainSingle(item => item.Id == "b1");
        Column(vm, BoardColumnKind.InProgress).Cards.Should().BeEmpty();
        toast.Items.Should().Contain(item => item.Level == ToastLevel.Error && item.Text == "写入被拒绝");
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task 拖到完成列会关闭任务()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(Bead("b1", "待办", "open"));

        var (vm, _) = CreateBoard(store);
        await vm.RefreshAsync();

        await vm.MoveAsync(FindCard(vm, "b1"), BoardColumnKind.Done);

        store.Beads.Single(bead => bead.Id == "b1").StatusRaw.Should().Be("closed");
        Column(vm, BoardColumnKind.Done).Cards.Should().ContainSingle(item => item.Id == "b1");
    }

    [Fact]
    public async Task 拖到阻塞列只提示不写库()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(Bead("b1", "待办", "open"));

        var (vm, toast) = CreateBoard(store);
        await vm.RefreshAsync();
        store.Calls.Clear();

        await vm.MoveAsync(FindCard(vm, "b1"), BoardColumnKind.Blocked);

        store.Calls.Should().BeEmpty();
        toast.Items.Should().Contain(item => item.Level == ToastLevel.Warning);
        Column(vm, BoardColumnKind.Ready).Cards.Should().ContainSingle(item => item.Id == "b1");
    }

    [Fact]
    public async Task 加载失败保留上次快照并提示()
    {
        var inner = new InMemoryBdStore();
        inner.Beads.Add(Bead("b1", "待办", "open"));
        var store = new FaultyBdStore(inner);
        var toast = new ToastService();
        var (vm, _) = CreateBoard(store, toast);

        await vm.RefreshAsync();
        var loadedAt = vm.LastLoadedText;

        store.ListError = new InvalidOperationException("读取失败");
        await vm.RefreshAsync();

        vm.ErrorMessage.Should().Be("读取失败");
        vm.TotalCount.Should().Be(1);
        vm.LastLoadedText.Should().Be(loadedAt);
        vm.IsBusy.Should().BeFalse();
        toast.Items.Should().Contain(item => item.Level == ToastLevel.Error && item.Text == "读取失败");
    }

    [Fact]
    public async Task 树状视图按父子关系递归挂载并缩进()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(Bead("p1", "父任务", "open"));
        store.Beads.Add(Bead("c1", "子任务", "open", parent: "p1"));
        store.Beads.Add(Bead("g1", "孙任务", "open", parent: "c1"));
        store.Beads.Add(Bead("t1", "独立任务", "open"));

        var (vm, _) = CreateBoard(store);
        await vm.RefreshAsync();

        vm.TreeRoots.Select(node => node.Id).Should().BeEquivalentTo(["p1", "t1"]);

        var parent = vm.TreeRoots.Single(node => node.Id == "p1");
        parent.Depth.Should().Be(0);
        parent.IndentWidth.Should().Be(0);
        parent.HasChildren.Should().BeTrue();
        parent.ChildCountText.Should().Be("1 项子任务");

        var child = parent.Children.Should().ContainSingle().Which;
        child.Id.Should().Be("c1");
        child.Depth.Should().Be(1);
        child.IndentWidth.Should().Be(18);
        child.Children.Should().ContainSingle().Which.Depth.Should().Be(2);

        vm.TreeRoots.Single(node => node.Id == "t1").HasChildren.Should().BeFalse();
        vm.IsTreeEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task 树状视图默认展开顶层深层折叠()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(Bead("p1", "父任务", "open"));
        store.Beads.Add(Bead("c1", "子任务", "open", parent: "p1"));
        store.Beads.Add(Bead("g1", "孙任务", "open", parent: "c1"));

        var (vm, _) = CreateBoard(store);
        await vm.RefreshAsync();

        vm.TreeRows.Select(node => node.Id).Should().Equal("p1", "c1");
    }

    [Fact]
    public async Task 树状视图展开折叠切换可见行()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(Bead("p1", "父任务", "open"));
        store.Beads.Add(Bead("c1", "子任务", "open", parent: "p1"));
        store.Beads.Add(Bead("g1", "孙任务", "open", parent: "c1"));

        var (vm, _) = CreateBoard(store);
        await vm.RefreshAsync();

        vm.ExpandAll();
        vm.TreeRows.Select(node => node.Id).Should().Equal("p1", "c1", "g1");

        vm.TreeRows.Single(node => node.Id == "c1").ToggleCommand.Execute(null);
        vm.TreeRows.Select(node => node.Id).Should().Equal("p1", "c1");

        vm.CollapseAll();
        vm.TreeRows.Select(node => node.Id).Should().Equal("p1");

        vm.TreeRoots[0].ToggleCommand.Execute(null);
        vm.TreeRows.Select(node => node.Id).Should().Equal("p1", "c1");
    }

    [Fact]
    public async Task 树状视图刷新后保留展开状态()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(Bead("p1", "父任务", "open"));
        store.Beads.Add(Bead("c1", "子任务", "open", parent: "p1"));

        var (vm, _) = CreateBoard(store);
        await vm.RefreshAsync();

        vm.CollapseAll();
        await vm.RefreshCoreAsync(CancellationToken.None, silent: true);

        vm.TreeRoots[0].IsExpanded.Should().BeFalse();
        vm.TreeRows.Select(node => node.Id).Should().Equal("p1");
    }

    [Fact]
    public async Task 树状视图把父任务缺失的任务升为根()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(Bead("c1", "孤儿任务", "open", parent: "gone"));
        store.Beads.Add(Bead("c2", "自引用任务", "open", parent: "c2"));

        var (vm, _) = CreateBoard(store);
        await vm.RefreshAsync();

        vm.TreeRoots.Select(node => node.Id).Should().BeEquivalentTo(["c1", "c2"]);
        vm.TreeRows.Should().HaveCount(2);
    }

    [Fact]
    public async Task 父子成环时不在树上无限递归()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(Bead("a", "任务 A", "open", parent: "b"));
        store.Beads.Add(Bead("b", "任务 B", "open", parent: "a"));

        var (vm, _) = CreateBoard(store);
        await vm.RefreshAsync();

        vm.ExpandAll();
        vm.TreeRows.Select(node => node.Id).Should().OnlyHaveUniqueItems();
        vm.TreeRows.Should().HaveCount(2);
    }

    private static (BoardViewModel Vm, ToastService Toast) CreateBoard(IBdStore store, ToastService? toast = null)
    {
        toast ??= new ToastService();
        var vm = new BoardViewModel(
            new BoardService(store),
            new BeadWriteService(store),
            new AttributionService(AppConfig.Default),
            toast);
        return (vm, toast);
    }

    private static Bead Bead(string id, string title, string status, string? parent = null) => new()
    {
        Id = id,
        Title = title,
        StatusRaw = status,
        TypeRaw = "task",
        PriorityValue = 2,
        Owner = "alice",
        CreatedBy = "alice",
        ParentId = parent,
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
    };

    private static BeadCardViewModel? FindCard(BoardViewModel vm, string id) =>
        vm.Columns.SelectMany(column => column.Cards).FirstOrDefault(card => card.Id == id);

    private static BoardColumnViewModel Column(BoardViewModel vm, BoardColumnKind kind) =>
        vm.Columns.Single(column => column.Kind == kind);
}
