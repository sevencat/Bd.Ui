using Bd.Core.Abstractions;
using Bd.Core.Config;
using Bd.Core.Domain;
using Bd.Core.Services;
using Bd.Testing.Fakes;
using FluentAssertions;
using Xunit;

namespace Bd.Core.Tests;

public sealed class DemoModeTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bd-ui-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task 演示数据可以填满全部界面()
    {
        var store = DemoDataProvider.CreateStore();

        var snapshot = await new BoardService(store).LoadAsync(CancellationToken.None);
        snapshot.Backlog.Should().NotBeEmpty("deferred 任务进入 Backlog");
        snapshot.Ready.Should().NotBeEmpty();
        snapshot.InProgress.Should().NotBeEmpty();
        snapshot.Blocked.Should().NotBeEmpty();
        snapshot.Done.Should().BeEmpty("看板只加载未关闭任务");

        var epics = await new EpicProgressService(store).LoadAsync(includeEmpty: false, CancellationToken.None);
        var epic = epics.Should().ContainSingle(item => item.Epic.Id == "demo-epic").Which;
        epic.Progress.Total.Should().Be(3);
        epic.Progress.Closed.Should().Be(1);

        var detail = await new BeadDetailService(store, new AttributionService(AppConfig.Default))
            .LoadAsync("demo-api", CancellationToken.None);
        detail.Comments.Should().HaveCount(2);
        detail.Bead.ParentId.Should().Be("demo-epic");
    }

    [Fact]
    public async Task 演示数据拒绝写入并提示原因()
    {
        var store = DemoDataProvider.CreateStore();
        var before = (await store.ListAsync(new BeadListQuery(), CancellationToken.None)).Count;

        var create = () => store.CreateAsync(new CreateBeadRequest { Title = "演示任务" }, CancellationToken.None);
        var exception = (await create.Should().ThrowAsync<BdException>()).Which;
        exception.Code.Should().Be(BdErrorCode.ReadOnly);
        exception.UserMessage.Should().Contain("演示数据");

        await FluentActions.Awaiting(() => store.CloseAsync("demo-api", "done", CancellationToken.None))
            .Should().ThrowAsync<BdException>();

        (await store.ListAsync(new BeadListQuery(), CancellationToken.None)).Should().HaveCount(before);
    }

    [Fact]
    public async Task 只读装饰器拦截全部写操作且读操作透传()
    {
        var inner = new InMemoryBdStore();
        inner.Beads.Add(new Bead { Id = "t-1", Title = "任务", StatusRaw = "open" });
        var guard = new ReadOnlyGuard(inner);

        (await guard.ListAsync(new BeadListQuery(), CancellationToken.None)).Should().ContainSingle();
        (await guard.GetAsync("t-1", CancellationToken.None)).Id.Should().Be("t-1");
        inner.Calls.Clear();

        Func<Task>[] writes =
        [
            () => guard.CreateAsync(new CreateBeadRequest { Title = "x" }, CancellationToken.None),
            () => guard.UpdateAsync("t-1", new UpdateBeadRequest { Title = "y" }, CancellationToken.None),
            () => guard.CloseAsync("t-1", null, CancellationToken.None),
            () => guard.DeleteAsync("t-1", CancellationToken.None),
            () => guard.AddCommentAsync("t-1", "note", CancellationToken.None),
            () => guard.AddLabelAsync("t-1", "demo", CancellationToken.None),
            () => guard.RemoveLabelAsync("t-1", "demo", CancellationToken.None),
            () => guard.AddDependencyAsync("t-1", "t-2", DependencyType.Blocks, CancellationToken.None),
            () => guard.RemoveDependencyAsync("t-1", "t-2", CancellationToken.None)
        ];

        foreach (var write in writes)
        {
            var exception = (await write.Should().ThrowAsync<BdException>()).Which;
            exception.Code.Should().Be(BdErrorCode.ReadOnly);
            exception.UserMessage.Should().Contain("只读");
        }

        inner.Calls.Should().BeEmpty("只读模式不得把写操作透传给内层");
        ReadOnlyGuard.Wrap(inner, readOnly: false).Should().BeSameAs(inner);
    }

    [Fact]
    public void 只有显式开启演示模式才使用内置数据()
    {
        DemoDataProvider.IsDemoRequested(new AppConfig { DemoMode = true }).Should().BeTrue();
        DemoDataProvider.IsDemoRequested(new AppConfig()).Should().BeFalse();
        DemoDataProvider.IsDemoRequested(new AppConfig { DoltHost = "10.0.0.9" }).Should().BeFalse();
    }

    [Fact]
    public async Task 演示数据的版本与自检可离线呈现()
    {
        var store = DemoDataProvider.CreateStore();

        (await store.GetVersionAsync(CancellationToken.None)).Should().Be(DemoDataProvider.Version);

        var report = await store.RunDoctorAsync(CancellationToken.None);
        report.Ok.Should().BeTrue();
        report.Summary.Should().Contain("演示");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
