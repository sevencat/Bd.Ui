using Bd.Core.Abstractions;
using Bd.Core.Domain;

namespace Bd.Core.Services;

/// <summary>
/// 只读装饰器：读操作全部透传，写操作在进入存储层之前立即拒绝并给出原因，
/// 保证只读配置下不会向 Dolt 发出任何写语句。
/// </summary>
public sealed class ReadOnlyGuard : IBdStore
{
    public const string DefaultReason = "只读模式：当前配置禁止写操作，请在「设置 → 界面与刷新」中关闭只读模式后重试。";

    private readonly IBdStore _inner;
    private readonly string _reason;

    public ReadOnlyGuard(IBdStore inner, string? reason = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _reason = string.IsNullOrWhiteSpace(reason) ? DefaultReason : reason.Trim();
    }

    public IBdStore Inner => _inner;

    public string Reason => _reason;

    public string BackendName => _inner.BackendName;

    /// <summary>需要只读时包一层，已包裹或不需要时原样返回。</summary>
    public static IBdStore Wrap(IBdStore store, bool readOnly, string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        return readOnly && store is not ReadOnlyGuard ? new ReadOnlyGuard(store, reason) : store;
    }

    public Task<IReadOnlyList<Bead>> ListAsync(BeadListQuery query, CancellationToken cancellationToken) =>
        _inner.ListAsync(query, cancellationToken);

    public Task<IReadOnlyList<Bead>> ReadyAsync(CancellationToken cancellationToken) =>
        _inner.ReadyAsync(cancellationToken);

    public Task<IReadOnlyList<Bead>> BlockedAsync(CancellationToken cancellationToken) =>
        _inner.BlockedAsync(cancellationToken);

    public Task<Bead> GetAsync(string id, CancellationToken cancellationToken) =>
        _inner.GetAsync(id, cancellationToken);

    public Task<IReadOnlyList<DependencyLink>> ListDependenciesAsync(string id, CancellationToken cancellationToken) =>
        _inner.ListDependenciesAsync(id, cancellationToken);

    public Task<IReadOnlyList<Comment>> ListCommentsAsync(string id, CancellationToken cancellationToken) =>
        _inner.ListCommentsAsync(id, cancellationToken);

    public Task<IReadOnlyList<BeadEvent>> ListEventsAsync(string id, CancellationToken cancellationToken) =>
        _inner.ListEventsAsync(id, cancellationToken);

    public Task<string> GetVersionAsync(CancellationToken cancellationToken) =>
        _inner.GetVersionAsync(cancellationToken);

    public Task<DoctorReport> RunDoctorAsync(CancellationToken cancellationToken) =>
        _inner.RunDoctorAsync(cancellationToken);

    public Task<string> CreateAsync(CreateBeadRequest request, CancellationToken cancellationToken) =>
        Reject<string>();

    public Task UpdateAsync(string id, UpdateBeadRequest request, CancellationToken cancellationToken) =>
        Reject();

    public Task CloseAsync(string id, string? reason, CancellationToken cancellationToken) =>
        Reject();

    public Task DeleteAsync(string id, CancellationToken cancellationToken) =>
        Reject();

    public Task AddCommentAsync(string id, string text, CancellationToken cancellationToken) =>
        Reject();

    public Task AddLabelAsync(string id, string label, CancellationToken cancellationToken) =>
        Reject();

    public Task RemoveLabelAsync(string id, string label, CancellationToken cancellationToken) =>
        Reject();

    public Task AddDependencyAsync(string id, string dependsOnId, DependencyType type, CancellationToken cancellationToken) =>
        Reject();

    public Task RemoveDependencyAsync(string id, string dependsOnId, CancellationToken cancellationToken) =>
        Reject();

    private Task<T> Reject<T>() => Task.FromException<T>(BdException.ReadOnly(_reason));

    private Task Reject() => Task.FromException(BdException.ReadOnly(_reason));
}
