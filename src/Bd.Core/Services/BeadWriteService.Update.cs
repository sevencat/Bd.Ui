using Bd.Core.Abstractions;
using Bd.Core.Domain;

namespace Bd.Core.Services;

public sealed partial class BeadWriteService
{
    public async Task UpdateAsync(string id, UpdateBeadRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(request);
        if (!request.HasChanges)
        {
            return;
        }

        var current = await _store.GetAsync(id, cancellationToken).ConfigureAwait(false);
        var patch = BuildPatch(current, request);
        if (!patch.HasChanges)
        {
            return;
        }

        await _store.UpdateAsync(id, patch, cancellationToken).ConfigureAwait(false);
        RaiseChanged(id);
    }

    public async Task TransitionAsync(string id, BeadStatus status, CancellationToken cancellationToken)
    {
        if (status == BeadStatus.Closed)
        {
            await CloseAsync(id, null, cancellationToken).ConfigureAwait(false);
            return;
        }

        await UpdateAsync(id, new UpdateBeadRequest { Status = BdEnumMaps.ToBdString(status) }, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task CloseAsync(string id, string? reason, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await _store.CloseAsync(id, reason, cancellationToken).ConfigureAwait(false);
        RaiseChanged(id);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await _store.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        RaiseChanged(id);
    }

    /// <summary>只把与当前值不同的字段发给 bd，避免无意义的写与并发覆盖。</summary>
    public static UpdateBeadRequest BuildPatch(Bead current, UpdateBeadRequest request)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(request);

        return new UpdateBeadRequest
        {
            Title = DiffText(current.Title, request.Title),
            Description = DiffText(current.Description, request.Description),
            Status = DiffText(current.StatusRaw, request.Status),
            Type = DiffText(current.TypeRaw, request.Type),
            Priority = request.Priority is { } priority && priority != current.PriorityValue ? priority : null,
            Assignee = DiffText(current.Owner, request.Assignee),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes
        };
    }

    private static string? DiffText(string? current, string? requested) =>
        requested is null || string.Equals(current ?? string.Empty, requested, StringComparison.Ordinal)
            ? null
            : requested;
}
