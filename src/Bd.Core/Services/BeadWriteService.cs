using Bd.Core.Abstractions;

namespace Bd.Core.Services;

/// <summary>写操作统一入口：成功后抛事件使看板/详情快照失效。</summary>
public sealed partial class BeadWriteService
{
    private readonly IBdStore _store;

    public BeadWriteService(IBdStore store) => _store = store ?? throw new ArgumentNullException(nameof(store));

    public event EventHandler<string>? BeadChanged;

    public async Task<string> CreateAsync(CreateBeadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new BdCli.BdException(BdCli.BdErrorCode.InvalidArgument, "任务标题不能为空");
        }

        var id = await _store.CreateAsync(request, cancellationToken).ConfigureAwait(false);
        RaiseChanged(id);
        return id;
    }

    public async Task<string> CreateChildAsync(string parentId, CreateBeadRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentId);
        ArgumentNullException.ThrowIfNull(request);
        return await CreateAsync(request with { ParentId = parentId }, cancellationToken).ConfigureAwait(false);
    }

    private void RaiseChanged(string id) => BeadChanged?.Invoke(this, id);
}
