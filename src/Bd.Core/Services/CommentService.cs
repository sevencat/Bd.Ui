using Bd.Core.Abstractions;
using Bd.Core.BdCli;
using Bd.Core.Domain;

namespace Bd.Core.Services;

public sealed class CommentService
{
    private readonly IBdStore _store;

    public CommentService(IBdStore store) => _store = store ?? throw new ArgumentNullException(nameof(store));

    public async Task<IReadOnlyList<Comment>> ListAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var comments = await _store.ListCommentsAsync(id, cancellationToken).ConfigureAwait(false);
        return [.. comments.OrderBy(static comment => comment.CreatedAt ?? DateTimeOffset.MinValue)];
    }

    public async Task AddAsync(string id, string text, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new BdException(BdErrorCode.InvalidArgument, "评论内容不能为空");
        }

        try
        {
            await _store.AddCommentAsync(id, text.Trim(), cancellationToken).ConfigureAwait(false);
        }
        catch (BdException ex) when (ex.Code == BdErrorCode.LockBusy)
        {
            await Task.Delay(150, cancellationToken).ConfigureAwait(false);
            await _store.AddCommentAsync(id, text.Trim(), cancellationToken).ConfigureAwait(false);
        }
    }
}
