using Bd.Core.Abstractions;
using Bd.Core.Domain;

namespace Bd.Core.Services;

public sealed record EpicProgressItem
{
    public required Bead Epic { get; init; }

    public required EpicProgress Progress { get; init; }

    public IReadOnlyList<Bead> Children { get; init; } = [];
}

/// <summary>只读派生：epic 的 closed/total 进度，不写入任何字段。</summary>
public sealed class EpicProgressService
{
    private readonly IBdStore _store;

    public EpicProgressService(IBdStore store) => _store = store ?? throw new ArgumentNullException(nameof(store));

    public async Task<IReadOnlyList<EpicProgressItem>> LoadAsync(bool includeEmpty, CancellationToken cancellationToken)
    {
        var activeTask = _store.ListAsync(new BeadListQuery(), cancellationToken);
        var closedTask = _store.ListAsync(new BeadListQuery { Status = "closed" }, cancellationToken);
        await Task.WhenAll(activeTask, closedTask).ConfigureAwait(false);

        var all = (await activeTask.ConfigureAwait(false))
            .Concat(await closedTask.ConfigureAwait(false))
            .GroupBy(static bead => bead.Id, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToList();

        var childrenByParent = all
            .Where(static bead => !string.IsNullOrWhiteSpace(bead.ParentId))
            .GroupBy(static bead => bead.ParentId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<Bead>)[.. group.OrderBy(bead => bead.PriorityValue).ThenBy(bead => bead.Id, StringComparer.Ordinal)],
                StringComparer.OrdinalIgnoreCase);

        var epics = all
            .Where(static bead => bead.IsEpic && !bead.IsArchived)
            .Select(epic =>
            {
                var children = childrenByParent.TryGetValue(epic.Id, out var found) ? found : [];
                return new EpicProgressItem
                {
                    Epic = epic,
                    Children = children,
                    Progress = new EpicProgress
                    {
                        Closed = children.Count(static child => child.IsClosed),
                        Total = children.Count
                    }
                };
            })
            .Where(item => includeEmpty || item.Progress.Total > 0)
            .OrderBy(static item => item.Progress.Ratio)
            .ThenBy(static item => item.Epic.PriorityValue)
            .ToList();

        return epics;
    }
}
