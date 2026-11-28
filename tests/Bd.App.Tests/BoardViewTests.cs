using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
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
    public async Task 树视图只渲染未关闭任务()
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

        vm.TotalCount.Should().Be(1);

        var window = TestScene.Show(new BoardView { DataContext = vm });
        var texts = window.Texts();

        texts.Should().Contain("登录页打磨");
        texts.Should().NotContain("已完成的活");
        window.Close();
    }

    [AvaloniaFact]
    public async Task 树面板上的刷新按钮把请求转给外壳()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(TestScene.Bead("b1", "任务"));

        var vm = new BoardViewModel(
            new BoardService(store),
            new BeadWriteService(store),
            new AttributionService(AppConfig.Default),
            new ToastService());

        await vm.RefreshAsync();

        var raised = 0;
        vm.RefreshRequested += (_, _) => raised++;

        var window = TestScene.Show(new BoardView { DataContext = vm });
        var button = window.GetVisualDescendants().OfType<Button>()
            .First(item => item.Content as string == "刷新");

        button.Command!.Execute(button.CommandParameter);

        raised.Should().Be(1);
        window.Close();
    }

    [AvaloniaFact]
    public async Task 树视图渲染父子层级并可折叠()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(TestScene.Bead("p1", "父任务"));
        store.Beads.Add(TestScene.Bead("c1", "子任务", parent: "p1"));

        var vm = new BoardViewModel(
            new BoardService(store),
            new BeadWriteService(store),
            new AttributionService(AppConfig.Default),
            new ToastService());

        await vm.RefreshAsync();

        var window = TestScene.Show(new BoardView { DataContext = vm });
        var texts = window.Texts();

        texts.Should().Contain("父任务");
        texts.Should().Contain("子任务");

        vm.CollapseAll();
        TestScene.Relayout(window);
        window.Texts().Should().Contain("父任务");
        window.Texts().Should().NotContain("子任务");

        vm.TreeRoots[0].ToggleCommand.Execute(null);
        TestScene.Relayout(window);
        window.Texts().Should().Contain("子任务");
        window.Close();
    }
}
