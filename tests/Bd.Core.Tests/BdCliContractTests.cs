using Bd.Core.Abstractions;
using Bd.Core.BdCli;
using Bd.Core.Domain;
using Bd.Core.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Bd.Core.Tests;

/// <summary>
/// 对真实 bd CLI 的合同测试：用 <see cref="TempBeadsRepo"/> 在临时目录建仓库，
/// 每个类共用一份仓库，测试内部各自新建任务以免互相依赖顺序。
/// </summary>
public sealed class BdCliContractTests : IClassFixture<TempBeadsRepo>
{
    private const string MissingId = "bdtest-missing";

    private readonly TempBeadsRepo _repo;
    private readonly SeedDataResult _seed;

    public BdCliContractTests(TempBeadsRepo repo)
    {
        _repo = repo;
        _seed = repo.SeedAsync().GetAwaiter().GetResult();
    }

    private IBdStore Store => _repo.Store;

    [BdFact]
    public async Task 列表与详情能读回种子数据()
    {
        var beads = await Store.ListAsync(new BeadListQuery { Limit = 200 }, CancellationToken.None);

        beads.Should().Contain(bead => bead.Id == _seed.TaskId && bead.Title == SeedData.TaskTitle);

        var task = await Store.GetAsync(_seed.TaskId, CancellationToken.None);
        task.Title.Should().Be(SeedData.TaskTitle);
        task.TypeRaw.Should().Be("bug");
        task.PriorityValue.Should().Be(2);
        task.Labels.Should().Contain(SeedData.SeedLabel);
    }

    [BdFact]
    public async Task Ready_与_Blocked_反映依赖关系()
    {
        var blocked = await Store.BlockedAsync(CancellationToken.None);
        blocked.Should().Contain(bead => bead.Id == _seed.TaskId);

        var ready = await Store.ReadyAsync(CancellationToken.None);
        ready.Should().Contain(bead => bead.Id == _seed.BlockerId);
        ready.Should().NotContain(bead => bead.Id == _seed.TaskId);
    }

    [BdFact]
    public async Task 搜索与状态过滤生效()
    {
        var bySearch = await Store.ListAsync(new BeadListQuery { Search = "种子任务", Limit = 200 }, CancellationToken.None);
        bySearch.Should().ContainSingle(bead => bead.Id == _seed.TaskId);

        var closed = await Store.ListAsync(new BeadListQuery { Status = "closed", Limit = 200 }, CancellationToken.None);
        closed.Should().NotContain(bead => bead.Id == _seed.TaskId);
    }

    [BdFact]
    public async Task 创建更新关闭形成闭环()
    {
        var id = await Store.CreateAsync(
            new CreateBeadRequest
            {
                Title = "合同测试：新建",
                Type = "task",
                Priority = 3,
                Labels = ["contract"]
            },
            CancellationToken.None);

        id.Should().StartWith(TempBeadsRepo.DefaultPrefix);

        await Store.UpdateAsync(
            id,
            new UpdateBeadRequest { Title = "合同测试：已更新", Priority = 0 },
            CancellationToken.None);

        var updated = await Store.GetAsync(id, CancellationToken.None);
        updated.Title.Should().Be("合同测试：已更新");
        updated.PriorityValue.Should().Be(0);

        await Store.CloseAsync(id, "合同测试收尾", CancellationToken.None);

        var closed = await Store.GetAsync(id, CancellationToken.None);
        closed.IsClosed.Should().BeTrue();
        closed.StatusRaw.Should().Be("closed");
    }

    [BdFact]
    public async Task 评论标签依赖可增可删()
    {
        var left = await Store.CreateAsync(new CreateBeadRequest { Title = "合同测试：左" }, CancellationToken.None);
        var right = await Store.CreateAsync(new CreateBeadRequest { Title = "合同测试：右" }, CancellationToken.None);

        await Store.AddCommentAsync(left, "合同测试评论", CancellationToken.None);
        (await Store.ListCommentsAsync(left, CancellationToken.None))
            .Should().Contain(comment => comment.Text == "合同测试评论");

        await Store.AddLabelAsync(left, "contract", CancellationToken.None);
        (await Store.GetAsync(left, CancellationToken.None)).Labels.Should().Contain("contract");

        await Store.RemoveLabelAsync(left, "contract", CancellationToken.None);
        (await Store.GetAsync(left, CancellationToken.None)).Labels.Should().NotContain("contract");

        await Store.AddDependencyAsync(left, right, DependencyType.Blocks, CancellationToken.None);
        var dependencies = await Store.ListDependenciesAsync(left, CancellationToken.None);
        dependencies.Should().Contain(link => link.TargetId == right && link.Type == DependencyType.Blocks);

        await Store.RemoveDependencyAsync(left, right, CancellationToken.None);
        (await Store.ListDependenciesAsync(left, CancellationToken.None))
            .Should().NotContain(link => link.TargetId == right);
    }

    [BdFact]
    public async Task 不存在的任务映射为_NotFound()
    {
        var act = () => Store.GetAsync(MissingId, CancellationToken.None);

        (await act.Should().ThrowAsync<BdException>()).Which.Code.Should().Be(BdErrorCode.NotFound);
    }

    [BdFact]
    public async Task 空标题映射为_InvalidArgument()
    {
        var act = () => Store.CreateAsync(new CreateBeadRequest { Title = "   " }, CancellationToken.None);

        (await act.Should().ThrowAsync<BdException>()).Which.Code.Should().Be(BdErrorCode.InvalidArgument);
    }

    [BdFact]
    public async Task 版本与仓库路径可读()
    {
        (await Store.GetVersionAsync(CancellationToken.None)).Should().MatchRegex(@"^\d+\.\d+");
        Directory.Exists(Path.Combine(_repo.RootPath, ".beads")).Should().BeTrue();
    }

    [BdFact]
    public async Task 两个临时仓库互不干扰()
    {
        using var other = new TempBeadsRepo();

        other.RootPath.Should().NotBe(_repo.RootPath);

        var otherId = await other.Store.CreateAsync(
            new CreateBeadRequest { Title = "另一个仓库的任务" },
            CancellationToken.None);

        var mine = await Store.ListAsync(new BeadListQuery { Limit = 200 }, CancellationToken.None);
        mine.Should().NotContain(bead => bead.Id == otherId);

        var theirs = await other.Store.ListAsync(new BeadListQuery { Limit = 200 }, CancellationToken.None);
        theirs.Should().Contain(bead => bead.Id == otherId);
    }
}
