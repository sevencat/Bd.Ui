using Bd.Core.Abstractions;
using Bd.Core.Domain;

namespace Bd.Core.Services;

/// <summary>archive = close + label archived；unarchive = label remove + reopen，均可逆且默认从看板隐藏。</summary>
public sealed class ArchiveService
{
    private readonly IBdStore _store;

    public ArchiveService(IBdStore store) => _store = store ?? throw new ArgumentNullException(nameof(store));

    public event EventHandler<string>? Archived;

    public async Task ArchiveAsync(string id, string? reason, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await _store.CloseAsync(id, reason ?? "archived", cancellationToken).ConfigureAwait(false);
        await _store.AddLabelAsync(id, BdLabels.Archived, cancellationToken).ConfigureAwait(false);
        Archived?.Invoke(this, id);
    }

    public async Task UnarchiveAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await _store.RemoveLabelAsync(id, BdLabels.Archived, cancellationToken).ConfigureAwait(false);
        await _store.UpdateAsync(
            id,
            new UpdateBeadRequest { Status = BdEnumMaps.ToBdString(BeadStatus.Open) },
            cancellationToken).ConfigureAwait(false);
        Archived?.Invoke(this, id);
    }

    public async Task<IReadOnlyList<Bead>> ListArchivedAsync(CancellationToken cancellationToken)
    {
        var beads = await _store
            .ListAsync(new BeadListQuery { Label = BdLabels.Archived }, cancellationToken)
            .ConfigureAwait(false);
        return [.. beads.Where(static bead => bead.IsArchived)];
    }
}
