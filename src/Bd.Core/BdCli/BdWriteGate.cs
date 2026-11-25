namespace Bd.Core.BdCli;

/// <summary>embedded 数据库只允许单写者，所有写命令在此串行化，等待过程可取消。</summary>
public sealed class BdWriteGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private int _activeWrites;

    public int ActiveWrites => Volatile.Read(ref _activeWrites);

    public bool IsIdle => ActiveWrites == 0;

    public async Task<T> RunAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _activeWrites);
        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _activeWrites);
            _semaphore.Release();
        }
    }

    public Task RunAsync(Func<Task> action, CancellationToken cancellationToken) =>
        RunAsync(
            async () =>
            {
                await action().ConfigureAwait(false);
                return true;
            },
            cancellationToken);
}
