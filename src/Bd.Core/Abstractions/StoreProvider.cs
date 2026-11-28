namespace Bd.Core.Abstractions;

/// <summary>可替换内层实现的存储代理，使设置变更无需重建 DI 容器。</summary>
public sealed class StoreProvider : IBdStore
{
    private IBdStore? _inner;

    public string BackendName => _inner?.BackendName ?? "unconfigured";

    public IBdStore Inner => _inner ?? throw new InvalidOperationException("存储尚未初始化，请先在设置中选择仓库");

    public bool IsConfigured => _inner is not null;

    public void Configure(IBdStore store) => _inner = store ?? throw new ArgumentNullException(nameof(store));

    public Task<IReadOnlyList<Domain.Bead>> ListAsync(BeadListQuery query, CancellationToken cancellationToken) =>
        Inner.ListAsync(query, cancellationToken);

    public Task<IReadOnlyList<Domain.Bead>> ReadyAsync(CancellationToken cancellationToken) =>
        Inner.ReadyAsync(cancellationToken);

    public Task<IReadOnlyList<Domain.Bead>> BlockedAsync(CancellationToken cancellationToken) =>
        Inner.BlockedAsync(cancellationToken);

    public Task<Domain.Bead> GetAsync(string id, CancellationToken cancellationToken) =>
        Inner.GetAsync(id, cancellationToken);

    public Task<IReadOnlyList<Domain.DependencyLink>> ListDependenciesAsync(string id, CancellationToken cancellationToken) =>
        Inner.ListDependenciesAsync(id, cancellationToken);

    public Task<IReadOnlyList<Domain.Comment>> ListCommentsAsync(string id, CancellationToken cancellationToken) =>
        Inner.ListCommentsAsync(id, cancellationToken);

    public Task<IReadOnlyList<Domain.BeadEvent>> ListEventsAsync(string id, CancellationToken cancellationToken) =>
        Inner.ListEventsAsync(id, cancellationToken);

    public Task<string> GetVersionAsync(CancellationToken cancellationToken) =>
        Inner.GetVersionAsync(cancellationToken);

    public Task<DoctorReport> RunDoctorAsync(CancellationToken cancellationToken) =>
        Inner.RunDoctorAsync(cancellationToken);

    public Task<string> CreateAsync(CreateBeadRequest request, CancellationToken cancellationToken) =>
        Inner.CreateAsync(request, cancellationToken);

    public Task UpdateAsync(string id, UpdateBeadRequest request, CancellationToken cancellationToken) =>
        Inner.UpdateAsync(id, request, cancellationToken);

    public Task CloseAsync(string id, string? reason, CancellationToken cancellationToken) =>
        Inner.CloseAsync(id, reason, cancellationToken);

    public Task DeleteAsync(string id, CancellationToken cancellationToken) =>
        Inner.DeleteAsync(id, cancellationToken);

    public Task AddCommentAsync(string id, string text, CancellationToken cancellationToken) =>
        Inner.AddCommentAsync(id, text, cancellationToken);

    public Task AddLabelAsync(string id, string label, CancellationToken cancellationToken) =>
        Inner.AddLabelAsync(id, label, cancellationToken);

    public Task RemoveLabelAsync(string id, string label, CancellationToken cancellationToken) =>
        Inner.RemoveLabelAsync(id, label, cancellationToken);

    public Task AddDependencyAsync(string id, string dependsOnId, Domain.DependencyType type, CancellationToken cancellationToken) =>
        Inner.AddDependencyAsync(id, dependsOnId, type, cancellationToken);

    public Task RemoveDependencyAsync(string id, string dependsOnId, CancellationToken cancellationToken) =>
        Inner.RemoveDependencyAsync(id, dependsOnId, cancellationToken);
}
