using Bd.Core.Abstractions;
using Bd.Core.Domain;
using Bd.Core.Views;

namespace Bd.Core.Services;

/// <summary>
/// 取一份任务快照并按列分列；失败时保留上次快照并抛结构化错误。
/// 默认只要未关闭任务，查询时由 SQL 直接过滤掉 closed，避免把整库历史拉回内存；
/// <paramref name="includeClosed"/> 为 true 时才把 closed 一起取回（用户显式切换后手动刷新）。
/// </summary>
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

    public Task<BoardSnapshot> LoadAsync(CancellationToken cancellationToken) =>
        LoadAsync(includeClosed: false, cancellationToken);

    public async Task<BoardSnapshot> LoadAsync(bool includeClosed, CancellationToken cancellationToken)
    {
        var beads = (await _store.ListAsync(
                new BeadListQuery { ExcludeClosed = !includeClosed },
                cancellationToken).ConfigureAwait(false))
            // 归档任务不参与展示（归档走 labels 约定标记）。
            .Where(static bead => !bead.IsArchived)
            // 聚合模式下不同库可能存在相同 ID，按「项目 + ID」去重。
            .GroupBy(static bead => (bead.Project, bead.Id))
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
