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
        // Epic 进度要统计 closed 子任务，所以这里必须取全量；一次查询已包含 closed，
        // 不需要再单独拉一遍 closed 快照。
        var all = (await _store.ListAsync(new BeadListQuery(), cancellationToken).ConfigureAwait(false))
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
