using Bd.Core.Abstractions;
using Bd.Core.BdCli;
using Bd.Core.Domain;

namespace Bd.Core.Services;

public sealed class DependencyService
{
    private readonly IBdStore _store;

    public DependencyService(IBdStore store) => _store = store ?? throw new ArgumentNullException(nameof(store));

    public event EventHandler<string>? DependenciesChanged;

    public async Task<IReadOnlyList<DependencyLink>> ListAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return await _store.ListDependenciesAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddAsync(string id, string dependsOnId, DependencyType type, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(dependsOnId);
        if (string.Equals(id, dependsOnId, StringComparison.OrdinalIgnoreCase))
        {
            throw new BdException(BdErrorCode.InvalidArgument, "任务不能依赖自身");
        }

        if (type == DependencyType.Blocks)
        {
            await EnsureNoCycleAsync(id, dependsOnId, cancellationToken).ConfigureAwait(false);
        }

        await _store.AddDependencyAsync(id, dependsOnId, type, cancellationToken).ConfigureAwait(false);
        DependenciesChanged?.Invoke(this, id);
    }

    public async Task RemoveAsync(string id, string dependsOnId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(dependsOnId);
        await _store.RemoveDependencyAsync(id, dependsOnId, cancellationToken).ConfigureAwait(false);
        DependenciesChanged?.Invoke(this, id);
    }

    public async Task<GraphData> LoadNeighborhoodAsync(string id, int depth, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var center = await _store.GetAsync(id, cancellationToken).ConfigureAwait(false);
        var beads = new Dictionary<string, Bead>(StringComparer.OrdinalIgnoreCase) { [center.Id] = center };

        var direct = await _store.ListDependenciesAsync(id, cancellationToken).ConfigureAwait(false);
        foreach (var link in direct)
        {
            if (string.IsNullOrWhiteSpace(link.TargetId) || beads.ContainsKey(link.TargetId))
            {
                continue;
            }

            try
            {
                beads[link.TargetId] = await _store.GetAsync(link.TargetId, cancellationToken).ConfigureAwait(false);
            }
            catch (BdException ex) when (ex.Code == BdErrorCode.NotFound)
            {
                // 目标已删除，跳过该边。
            }
        }

        var graph = DependencyGraph.From(beads.Values);
        var neighborhood = graph.Neighbors(id, Math.Max(1, depth));
        return graph.ToData([id, .. neighborhood]);
    }

    private async Task EnsureNoCycleAsync(string id, string dependsOnId, CancellationToken cancellationToken)
    {
        var beads = await _store.ListAsync(new BeadListQuery(), cancellationToken).ConfigureAwait(false);
        var graph = DependencyGraph.From(beads);
        if (graph.WouldCreateCycle(id, dependsOnId))
        {
            throw new BdException(BdErrorCode.InvalidArgument, $"添加 {id} → {dependsOnId} 会形成依赖环，已拒绝");
        }
    }
}
