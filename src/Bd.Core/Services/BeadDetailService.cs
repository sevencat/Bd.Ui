using Bd.Core.Abstractions;
using Bd.Core.Domain;

namespace Bd.Core.Services;

public sealed record EpicProgress
{
    public int Closed { get; init; }

    public int Total { get; init; }

    public double Ratio => Total <= 0 ? 0 : Math.Clamp((double)Closed / Total, 0, 1);

    public string Text => $"{Closed} / {Total}";
}

public sealed record BeadDetail
{
    public required Bead Bead { get; init; }

    public IReadOnlyList<DependencyLink> Dependencies { get; init; } = [];

    public IReadOnlyList<Comment> Comments { get; init; } = [];

    public IReadOnlyList<Bead> Children { get; init; } = [];

    public EpicProgress Progress { get; init; } = new();

    public Attribution Attribution { get; init; } = Attribution.Unknown;
}

/// <summary>组合 show + dep list + comments + 子项，id 不存在时抛 NotFound。</summary>
public sealed class BeadDetailService
{
    private readonly IBdStore _store;
    private readonly AttributionService _attribution;

    public BeadDetailService(IBdStore store, AttributionService attribution)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _attribution = attribution ?? throw new ArgumentNullException(nameof(attribution));
    }

    public async Task<BeadDetail> LoadAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var beadTask = _store.GetAsync(id, cancellationToken);
        var dependenciesTask = _store.ListDependenciesAsync(id, cancellationToken);
        var commentsTask = _store.ListCommentsAsync(id, cancellationToken);
        await Task.WhenAll(beadTask, dependenciesTask, commentsTask).ConfigureAwait(false);

        var bead = await beadTask.ConfigureAwait(false);
        var children = await LoadChildrenAsync(id, cancellationToken).ConfigureAwait(false);
        var progress = BuildProgress(children);

        return new BeadDetail
        {
            Bead = bead,
            Dependencies = await dependenciesTask.ConfigureAwait(false),
            Comments = await commentsTask.ConfigureAwait(false),
            Children = children,
            Progress = progress,
            Attribution = _attribution.Resolve(bead.CreatedBy)
        };
    }

    public async Task<IReadOnlyList<Bead>> LoadChildrenAsync(string parentId, CancellationToken cancellationToken)
    {
        var all = await _store.ListAsync(new BeadListQuery(), cancellationToken).ConfigureAwait(false);
        return
        [
            .. all.Where(bead => string.Equals(bead.ParentId, parentId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(static bead => bead.PriorityValue)
                .ThenBy(static bead => bead.Id, StringComparer.Ordinal)
        ];
    }

    public static EpicProgress BuildProgress(IEnumerable<Bead> children)
    {
        var total = 0;
        var closed = 0;
        foreach (var child in children)
        {
            total++;
            if (child.IsClosed)
            {
                closed++;
            }
        }

        return new EpicProgress { Closed = closed, Total = total };
    }
}
