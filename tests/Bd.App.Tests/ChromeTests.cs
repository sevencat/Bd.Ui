using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Bd.App.Controls;
using Bd.Core.Domain;
using Bd.Core.Services;
using Bd.Core.ViewModels;
using Bd.Core.Views;
using Bd.Testing.Fakes;
using FluentAssertions;
using Xunit;

namespace Bd.App.Tests;

public sealed class ChromeTests
{
    [AvaloniaFact]
    public void 提示浮层显示消息并可关闭()
    {
        var toast = new ToastService();
        var window = TestScene.Show(new ToastHost { DataContext = toast }, 600, 400);

        toast.Success("已保存");
        TestScene.Relayout(window);

        window.Texts().Should().Contain("已保存");
        toast.Items.Should().HaveCount(1);

        toast.Dismiss(toast.Items[0]);
        toast.Items.Should().BeEmpty();
        window.Close();
    }

    [AvaloniaFact]
    public void 依赖图点击节点触发选择()
    {
        var graph = DependencyGraph.From(
        [
            TestScene.Bead("a", "根节点"),
            TestScene.Bead("b", "依赖项")
        ]);
        graph.AddEdge("a", "b", DependencyType.Blocks);

        var (nodes, width, height) = GraphLayout.Layout(graph.ToData(), "a");
        var control = new DependencyGraphControl
        {
            Nodes = nodes,
            Edges = graph.Edges,
            RootId = "a",
            Width = width,
            Height = height
        };

        string? selected = null;
        control.NodeSelected += (_, id) => selected = id;

        var window = TestScene.Show(control, width + 40, height + 40);
        window.CaptureRenderedFrame();

        var box = nodes.First(node => node.Id == "a");
        var point = control.TranslatePoint(new Point(box.X + 12, box.Y + 12), window)!.Value;

        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);

        selected.Should().Be("a");
        window.Close();
    }

    [AvaloniaFact]
    public void 依赖图拖拽节点发出连线请求()
    {
        var graph = DependencyGraph.From(
        [
            TestScene.Bead("a", "根节点"),
            TestScene.Bead("b", "依赖项")
        ]);
        graph.AddEdge("a", "b", DependencyType.Blocks);

        var (nodes, width, height) = GraphLayout.Layout(graph.ToData(), "a");
        var control = new DependencyGraphControl
        {
            Nodes = nodes,
            Edges = graph.Edges,
            RootId = "a",
            Width = width,
            Height = height
        };

        GraphConnectionRequest? request = null;
        control.ConnectionRequested += (_, value) => request = value;

        var window = TestScene.Show(control, width + 40, height + 40);
        window.CaptureRenderedFrame();

        var from = control.TranslatePoint(Center(nodes, "a"), window)!.Value;
        var to = control.TranslatePoint(Center(nodes, "b"), window)!.Value;

        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        window.MouseUp(to, MouseButton.Left);

        request.Should().NotBeNull();
        request!.SourceId.Should().Be("a");
        request.TargetId.Should().Be("b");
        window.Close();
    }

    [Fact]
    public async Task 拖拽连线成环时被拒绝并提示()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(TestScene.Bead("a", "根节点"));
        store.Beads.Add(TestScene.Bead("b", "依赖项"));
        await store.AddDependencyAsync("a", "b", DependencyType.Blocks, CancellationToken.None);

        var toast = new ToastService();
        var vm = new GraphViewModel(new DependencyService(store), store, toast);

        await vm.ConnectAsync("b", "a", "blocks");

        vm.ErrorMessage.Should().Contain("环");
        toast.Items.Should().Contain(item => item.Text.Contains("环"));
        store.Beads.Single(bead => bead.Id == "b").Dependencies.Should().BeEmpty();
    }

    [Fact]
    public async Task 拖拽连线成功后写入依赖()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(TestScene.Bead("a", "根节点"));
        store.Beads.Add(TestScene.Bead("b", "依赖项"));

        var toast = new ToastService();
        var vm = new GraphViewModel(new DependencyService(store), store, toast);

        await vm.ConnectAsync("a", "b", "related");

        store.Beads.Single(bead => bead.Id == "a").Dependencies
            .Should().ContainSingle(link => link.TargetId == "b");
        toast.Items.Should().Contain(item => item.Level == ToastLevel.Success);
    }

    private static Point Center(IReadOnlyList<GraphNodeBox> nodes, string id)
    {
        var box = nodes.First(node => node.Id == id);
        return new Point(box.X + 12, box.Y + 12);
    }
}
