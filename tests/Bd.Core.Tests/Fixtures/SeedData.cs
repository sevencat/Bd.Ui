using Bd.Core.Abstractions;
using Bd.Core.Domain;

namespace Bd.Core.Tests.Fixtures;

public sealed record SeedDataResult(string EpicId, string BlockerId, string TaskId);

/// <summary>写入合同测试所需的最小种子：1 Epic + 1 阻塞项 + 1 被阻塞任务（含依赖/评论/标签）。</summary>
public static class SeedData
{
    public const string TaskTitle = "种子任务：被阻塞";
    public const string BlockerTitle = "种子阻塞项";
    public const string EpicTitle = "种子 Epic";
    public const string SeedComment = "种子评论";
    public const string SeedLabel = "seed";

    public static async Task<SeedDataResult> PopulateAsync(IBdStore store, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);

        var epicId = await store.CreateAsync(
            new CreateBeadRequest
            {
                Title = EpicTitle,
                Type = "epic",
                Priority = 1,
                Description = "种子 Epic 描述"
            },
            cancellationToken).ConfigureAwait(false);

        var blockerId = await store.CreateAsync(
            new CreateBeadRequest
            {
                Title = BlockerTitle,
                Type = "task",
                Priority = 1,
                Assignee = "alice",
                Labels = [SeedLabel]
            },
            cancellationToken).ConfigureAwait(false);

        var taskId = await store.CreateAsync(
            new CreateBeadRequest
            {
                Title = TaskTitle,
                Type = "bug",
                Priority = 2,
                Description = "种子任务描述",
                ParentId = epicId,
                Labels = [SeedLabel, "ui"]
            },
            cancellationToken).ConfigureAwait(false);

        await store.AddDependencyAsync(taskId, blockerId, DependencyType.Blocks, cancellationToken).ConfigureAwait(false);
        await store.AddCommentAsync(taskId, SeedComment, cancellationToken).ConfigureAwait(false);

        return new SeedDataResult(epicId, blockerId, taskId);
    }
}
