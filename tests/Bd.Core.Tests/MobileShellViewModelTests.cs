using Bd.Core.Abstractions;
using Bd.Core.Config;
using Bd.Core.Domain;
using Bd.Core.Dolt;
using Bd.Core.Services;
using Bd.Core.ViewModels;
using Bd.Testing.Fakes;
using FluentAssertions;
using Xunit;

namespace Bd.Core.Tests;

/// <summary>手机端外壳的行为：连库、加载任务树、把失败转成可读文案。</summary>
public sealed class MobileShellViewModelTests
{
    [Fact]
    public async Task 初始化后加载任务树并更新状态()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(Bead("b1", "待办", "open"));
        store.Beads.Add(Bead("b2", "进行中", "in_progress"));

        var (vm, _) = Create(store);
        await vm.InitializeAsync();

        vm.IsInitialized.Should().BeTrue();
        vm.IsBusy.Should().BeFalse();
        vm.ErrorMessage.Should().BeNull();
        vm.Board.TreeRows.Should().HaveCount(2);
        vm.StatusText.Should().Contain("2 项");
        vm.VersionText.Should().Be("dolt 1.3.4");
    }

    [Fact]
    public async Task 没有任务时状态显示暂无任务()
    {
        var (vm, _) = Create(new InMemoryBdStore());
        await vm.InitializeAsync();

        vm.Board.TreeRows.Should().BeEmpty();
        vm.StatusText.Should().Be("暂无任务");
    }

    [Fact]
    public async Task 初始化失败时给出错误并保持未初始化()
    {
        var (vm, _) = Create(new InMemoryBdStore(), failLoad: true);
        await vm.InitializeAsync();

        vm.IsInitialized.Should().BeFalse();
        vm.IsBusy.Should().BeFalse();
        // 手机端没有 toast 宿主，失败信息直接落在列表上方的错误条里。
        vm.ErrorMessage.Should().Be("连不上 Dolt");
        vm.StatusText.Should().Contain("初始化失败");
    }

    [Fact]
    public async Task 存储未配置时刷新直接返回()
    {
        var (vm, _) = Create(new InMemoryBdStore(), configureStore: false);

        await vm.RefreshAsync();

        vm.IsBusy.Should().BeFalse();
        vm.Board.TreeRows.Should().BeEmpty();
    }

    [Fact]
    public async Task 点选任务后进入详情页()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(Bead("b1", "待办", "open"));

        var (vm, _) = Create(store);
        await vm.InitializeAsync();

        await vm.OpenRowAsync(vm.Board.TreeRows.Single());

        vm.Screen.Should().Be(MobileScreen.Detail);
        vm.IsDetailVisible.Should().BeTrue();
        vm.IsListVisible.Should().BeFalse();
        vm.CanGoBack.Should().BeTrue();
        vm.Detail.IsOpen.Should().BeTrue();
        vm.Detail.Title.Should().Be("待办");
        vm.LoadingText.Should().Be("正在加载 b1");
    }

    [Fact]
    public async Task 返回键回到列表并清掉选择()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(Bead("b1", "待办", "open"));

        var (vm, _) = Create(store);
        await vm.InitializeAsync();
        await vm.OpenRowAsync(vm.Board.TreeRows.Single());

        vm.GoBack();

        vm.Screen.Should().Be(MobileScreen.List);
        vm.IsListVisible.Should().BeTrue();
        vm.CanGoBack.Should().BeFalse();
        vm.Board.SelectedRow.Should().BeNull();
    }

    [Fact]
    public async Task 详情加载失败时退回列表并提示()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(Bead("b1", "待办", "open"));

        var (vm, _) = Create(store);
        await vm.InitializeAsync();

        await vm.OpenDetailAsync("missing");

        vm.Screen.Should().Be(MobileScreen.List);
        vm.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task 详情里跑子任务会换成子任务详情()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(Bead("b1", "父", "open"));
        store.Beads.Add(Bead("b2", "子", "in_progress", parent: "b1"));

        var (vm, _) = Create(store);
        await vm.InitializeAsync();
        await vm.OpenDetailAsync("b1");

        vm.Detail.Children.Should().ContainSingle();
        vm.Detail.OpenChild(vm.Detail.Children[0]);

        // OpenChild 走 NavigateRequested 事件，异步加载完才切屏。
        await WaitForAsync(() => vm.Detail.Id == "b2" && vm.Screen == MobileScreen.Detail);

        vm.Detail.Title.Should().Be("子");
    }

    private static (MobileShellViewModel Vm, ToastService Toast) Create(
        InMemoryBdStore store,
        bool failLoad = false,
        bool configureStore = true)
    {
        var toast = new ToastService();
        var provider = new StoreProvider();

        if (configureStore)
        {
            provider.Configure(store);
        }

        var configStore = new MemoryConfigStore(AppConfig.Default, failLoad);
        var session = new AppSession(
            configStore,
            _ => store,
            provider,
            new AttributionService(AppConfig.Default));

        var board = new BoardViewModel(
            new BoardService(provider),
            new BeadWriteService(provider),
            new AttributionService(AppConfig.Default),
            toast);

        var attribution = new AttributionService(AppConfig.Default);
        var detail = new BeadDetailViewModel(
            new BeadDetailService(provider, attribution),
            new BeadWriteService(provider),
            new CommentService(provider),
            new ArchiveService(provider),
            toast);

        var settings = new MobileSettingsViewModel(session, configStore, new DoltConnectionFactory(), toast);

        return (new MobileShellViewModel(board, detail, settings, session, provider, toast), toast);
    }

    [Fact]
    public async Task 打开设置页后返回键回到列表()
    {
        var (vm, _) = Create(new InMemoryBdStore());
        await vm.InitializeAsync();

        vm.OpenSettings();

        vm.Screen.Should().Be(MobileScreen.Settings);
        vm.IsSettingsVisible.Should().BeTrue();
        vm.IsListVisible.Should().BeFalse();
        vm.CanGoBack.Should().BeTrue();

        vm.GoBack();

        vm.Screen.Should().Be(MobileScreen.List);
    }

    [Fact]
    public async Task 设置保存后回列表并重新拉数据()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(Bead("b1", "待办", "open"));

        var (vm, _) = Create(store);
        await vm.InitializeAsync();
        vm.OpenSettings();

        // 模拟别人在这期间新建了任务：保存后应看到 2 条。
        store.Beads.Add(Bead("b2", "新任务", "open"));
        await vm.Settings.SaveAsync();

        await WaitForAsync(() => vm.Screen == MobileScreen.List && vm.Board.TreeRows.Count == 2);

        vm.Board.TreeRows.Should().HaveCount(2);
        vm.Settings.StatusMessage.Should().Contain("已保存");
    }

    /// <summary>事件触发的异步导航没有可等待的句柄，这里轮询到位为止。</summary>
    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("等待导航完成超时");
            }

            await Task.Delay(10);
        }
    }

    private static Bead Bead(string id, string title, string status, string? parent = null) => new()
    {
        Id = id,
        Title = title,
        StatusRaw = status,
        TypeRaw = "task",
        PriorityValue = 2,
        Owner = "alice",
        CreatedAt = new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero),
        ParentId = parent
    };

    private sealed class MemoryConfigStore(AppConfig initial, bool failLoad) : IAppConfigStore
    {
        public string FilePath => "memory://bd-ui/config.json";

        public Task<AppConfig> LoadAsync(CancellationToken cancellationToken) =>
            failLoad
                ? Task.FromException<AppConfig>(new BdException(BdErrorCode.Connection, "连不上 Dolt"))
                : Task.FromResult(initial);

        public Task SaveAsync(AppConfig config, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
