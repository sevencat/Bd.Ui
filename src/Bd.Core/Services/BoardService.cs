using Bd.Core.Abstractions;
using Bd.Core.Domain;
using Bd.Core.Filters;
using Bd.Core.Views;

namespace Bd.Core.Services;

/// <summary>聚合 list 与 closed 两次快照，按五列分列；失败时保留上次快照并抛结构化错误。</summary>
public sealed class BoardService
{
    private readonly IBdStore _store;
    private readonly object _sync = new();
    private BoardSnapshot? _lastSnapshot;

    public BoardService(IBdStore store) => _store = store ?? throw new ArgumentNullException(nameof(store));

    public event EventHandler? SnapshotChanged;

    public BoardSnapshot? LastSnapshot
    {
        get
        {
            lock (_sync)
            {
                return _lastSnapshot;
            }
        }
    }

    public async Task<BoardSnapshot> LoadAsync(BoardFilter? filter, CancellationToken cancellationToken)
    {
        filter ??= new BoardFilter();
        var activeTask = _store.ListAsync(new BeadListQuery(), cancellationToken);
        var closedTask = _store.ListAsync(new BeadListQuery { Status = "closed" }, cancellationToken);
        await Task.WhenAll(activeTask, closedTask).ConfigureAwait(false);

        var active = await activeTask.ConfigureAwait(false);
        var closed = await closedTask.ConfigureAwait(false);

        var beads = active
            .Concat(closed)
            .Where(filter.Matches)
            .GroupBy(static bead => bead.Id, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToList();

        var snapshot = Build(beads, DateTimeOffset.UtcNow);
        lock (_sync)
        {
            _lastSnapshot = snapshot;
        }

        SnapshotChanged?.Invoke(this, EventArgs.Empty);
        return snapshot;
    }

    public static BoardSnapshot Build(IEnumerable<Bead> beads, DateTimeOffset loadedAt)
    {
        var backlog = new List<Bead>();
        var ready = new List<Bead>();
        var inProgress = new List<Bead>();
        var blocked = new List<Bead>();
        var done = new List<Bead>();

        foreach (var bead in beads)
        {
            switch (BeadViewHelpers.ResolveColumn(bead))
            {
                case BoardColumnKind.Backlog:
                    backlog.Add(bead);
                    break;
                case BoardColumnKind.Ready:
                    ready.Add(bead);
                    break;
                case BoardColumnKind.InProgress:
                    inProgress.Add(bead);
                    break;
                case BoardColumnKind.Blocked:
                    blocked.Add(bead);
                    break;
                default:
                    done.Add(bead);
                    break;
            }
        }

        return new BoardSnapshot
        {
            Backlog = BeadViewHelpers.SortForColumn(backlog),
            Ready = BeadViewHelpers.SortForColumn(ready),
            InProgress = BeadViewHelpers.SortForColumn(inProgress),
            Blocked = BeadViewHelpers.SortForColumn(blocked),
            Done = BeadViewHelpers.SortForColumn(done),
            LoadedAt = loadedAt
        };
    }

    public void Invalidate()
    {
        lock (_sync)
        {
            _lastSnapshot = null;
        }
    }
}
