using Avalonia.Headless.XUnit;
using Bd.App.Views;
using Bd.Core.Config;
using Bd.Core.Domain;
using Bd.Core.Services;
using Bd.Core.ViewModels;
using Bd.Testing.Fakes;
using FluentAssertions;
using Xunit;

namespace Bd.App.Tests;

public sealed class DetailAndEditorTests
{
    [AvaloniaFact]
    public async Task 详情抽屉显示标题与评论()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(TestScene.Bead("b1", "详情标题"));
        store.Comments.Add(new Comment
        {
            Id = "c1",
            IssueId = "b1",
            Author = "alice",
            Text = "第一条评论",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var vm = new BeadDetailViewModel(
            new BeadDetailService(store, new AttributionService(AppConfig.Default)),
            new BeadWriteService(store),
            new CommentService(store),
            new ArchiveService(store),
            new ToastService());

        await vm.LoadAsync("b1");
        vm.IsOpen.Should().BeTrue();

        var window = TestScene.Show(new BeadDetailView { DataContext = vm });
        var texts = window.Texts();

        texts.Should().Contain("详情标题");
        texts.Should().Contain("第一条评论");
        window.Close();
    }

    [AvaloniaFact]
    public async Task 详情渲染依赖与关联两组()
    {
        var store = new InMemoryBdStore();
        store.Beads.Add(TestScene.Bead("b1", "详情标题"));
        store.Beads.Add(TestScene.Bead("dep1", "阻塞项"));
        store.Beads.Add(TestScene.Bead("rel1", "关联项"));
        await store.AddDependencyAsync("b1", "dep1", DependencyType.Blocks, CancellationToken.None);
        await store.AddDependencyAsync("b1", "rel1", DependencyType.Related, CancellationToken.None);

        var vm = new BeadDetailViewModel(
            new BeadDetailService(store, new AttributionService(AppConfig.Default)),
            new BeadWriteService(store),
            new CommentService(store),
            new ArchiveService(store),
            new ToastService());

        await vm.LoadAsync("b1");

        var window = TestScene.Show(new BeadDetailView { DataContext = vm });
        var texts = window.Texts();

        texts.Should().Contain("Depends On");
        texts.Should().Contain("Related");
        texts.Should().Contain("dep1: 阻塞项");
        texts.Should().Contain("rel1: 关联项");
        window.Close();
    }

    [AvaloniaFact]
    public async Task 新建对话框拒绝空标题并创建成功()
    {
        var store = new InMemoryBdStore();
        var vm = new BeadEditorViewModel(new BeadWriteService(store), new ToastService());
        var dialog = new BeadEditorDialog { DataContext = vm };
        string? createdId = null;
        vm.Created += (_, id) => createdId = id;

        await vm.SaveAsync();
        vm.ErrorText.Should().Be("标题不能为空");
        createdId.Should().BeNull();

        vm.Title = "新任务";
        await vm.SaveAsync();

        createdId.Should().NotBeNullOrEmpty();
        store.Beads.Should().ContainSingle(bead => bead.Title == "新任务");
    }
}
