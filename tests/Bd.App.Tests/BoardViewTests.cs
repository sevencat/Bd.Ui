using Avalonia.Headless.XUnit;
using Bd.App.Views;
using Bd.Core.Config;
using Bd.Core.Services;
using Bd.Core.ViewModels;
using Bd.Testing.Fakes;
using FluentAssertions;
using Xunit;

namespace Bd.App.Tests;

public sealed class BoardViewTests
{
    [AvaloniaFact]
    public async Task 看板渲染五列并显示卡片标题()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(TestScene.Bead("b1", "登录页打磨"));
        store.Beads.Add(TestScene.Bead("b2", "已完成的活", status: "closed"));

        var toast = new ToastService();
        var vm = new BoardViewModel(
            new BoardService(store),
            new BeadWriteService(store),
            new AttributionService(AppConfig.Default),
            toast);

        await vm.RefreshAsync();

        vm.Columns.Should().HaveCount(5);
        vm.TotalCount.Should().Be(2);

        var window = TestScene.Show(new BoardView { DataContext = vm });
        var texts = window.Texts();

        texts.Should().Contain("登录页打磨");
        texts.Should().Contain("已完成的活");
        window.Close();
    }

    [AvaloniaFact]
    public async Task 过滤后只渲染命中的卡片()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(TestScene.Bead("b1", "登录页打磨"));
        store.Beads.Add(TestScene.Bead("b2", "依赖图渲染"));

        var vm = new BoardViewModel(
            new BoardService(store),
            new BeadWriteService(store),
            new AttributionService(AppConfig.Default),
            new ToastService());

        await vm.RefreshAsync();
        vm.Filter.SearchText = "依赖图";
        await vm.RefreshCoreAsync(CancellationToken.None, silent: true);

        var window = TestScene.Show(new BoardView { DataContext = vm });
        var texts = window.Texts();

        texts.Should().Contain("依赖图渲染");
        texts.Should().NotContain("登录页打磨");
        window.Close();
    }
}
