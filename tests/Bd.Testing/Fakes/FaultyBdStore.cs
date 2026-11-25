using Bd.Core.Abstractions;
using Bd.Core.Domain;

namespace Bd.Testing.Fakes;

/// <summary>
/// 可注入失败的存储包装：读与写可分别抛错，写调用计数用于断言"失败后没有重试/串写"。
/// 未注入错误时行为等同 <see cref="InMemoryBdStore"/>。
/// </summary>
public sealed class FaultyBdStore : IBdStore
{
    private readonly InMemoryBdStore _inner;

    public FaultyBdStore(InMemoryBdStore? inner = null) => _inner = inner ?? new InMemoryBdStore();

    public InMemoryBdStore Inner => _inner;

    /// <summary>设置后，所有读取抛出该异常。</summary>
    public Exception? ListError { get; set; }

    /// <summary>设置后，所有写入抛出该异常。</summary>
    public Exception? WriteError { get; set; }

    public int WriteCalls { get; private set; }

    public string BackendName => _inner.BackendName;

    public Task<IReadOnlyList<Bead>> ListAsync(BeadListQuery query, CancellationToken cancellationToken)
    {
        ThrowIfListFails();
        return _inner.ListAsync(query, cancellationToken);
    }

    public Task<IReadOnlyList<Bead>> ReadyAsync(CancellationToken cancellationToken)
    {
        ThrowIfListFails();
        return _inner.ReadyAsync(cancellationToken);
    }

    public Task<IReadOnlyList<Bead>> BlockedAsync(CancellationToken cancellationToken)
    {
        ThrowIfListFails();
        return _inner.BlockedAsync(cancellationToken);
    }

    public Task<Bead> GetAsync(string id, CancellationToken cancellationToken)
    {
        ThrowIfListFails();
        return _inner.GetAsync(id, cancellationToken);
    }

    public Task<IReadOnlyList<DependencyLink>> ListDependenciesAsync(string id, CancellationToken cancellationToken)
    {
        ThrowIfListFails();
        return _inner.ListDependenciesAsync(id, cancellationToken);
    }

    public Task<IReadOnlyList<Comment>> ListCommentsAsync(string id, CancellationToken cancellationToken)
    {
        ThrowIfListFails();
        return _inner.ListCommentsAsync(id, cancellationToken);
    }

    public Task<string> GetVersionAsync(CancellationToken cancellationToken) => _inner.GetVersionAsync(cancellationToken);

    public Task<DoctorReport> RunDoctorAsync(CancellationToken cancellationToken) => _inner.RunDoctorAsync(cancellationToken);

    public Task<string> CreateAsync(CreateBeadRequest request, CancellationToken cancellationToken)
    {
        ThrowIfWriteFails();
        return _inner.CreateAsync(request, cancellationToken);
    }

    public Task UpdateAsync(string id, UpdateBeadRequest request, CancellationToken cancellationToken)
    {
        ThrowIfWriteFails();
        return _inner.UpdateAsync(id, request, cancellationToken);
    }

    public Task CloseAsync(string id, string? reason, CancellationToken cancellationToken)
    {
        ThrowIfWriteFails();
        return _inner.CloseAsync(id, reason, cancellationToken);
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        ThrowIfWriteFails();
        return _inner.DeleteAsync(id, cancellationToken);
    }

    public Task AddCommentAsync(string id, string text, CancellationToken cancellationToken)
    {
        ThrowIfWriteFails();
        return _inner.AddCommentAsync(id, text, cancellationToken);
    }

    public Task AddLabelAsync(string id, string label, CancellationToken cancellationToken)
    {
        ThrowIfWriteFails();
        return _inner.AddLabelAsync(id, label, cancellationToken);
    }

    public Task RemoveLabelAsync(string id, string label, CancellationToken cancellationToken)
    {
        ThrowIfWriteFails();
        return _inner.RemoveLabelAsync(id, label, cancellationToken);
    }

    public Task AddDependencyAsync(string id, string dependsOnId, DependencyType type, CancellationToken cancellationToken)
    {
        ThrowIfWriteFails();
        return _inner.AddDependencyAsync(id, dependsOnId, type, cancellationToken);
    }

    public Task RemoveDependencyAsync(string id, string dependsOnId, CancellationToken cancellationToken)
    {
        ThrowIfWriteFails();
        return _inner.RemoveDependencyAsync(id, dependsOnId, cancellationToken);
    }

    private void ThrowIfListFails()
    {
        if (ListError is not null)
        {
            throw ListError;
        }
    }

    private void ThrowIfWriteFails()
    {
        WriteCalls++;
        if (WriteError is not null)
        {
            throw WriteError;
        }
    }
}
