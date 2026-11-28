using Bd.Core.Abstractions;

namespace Bd.Core.Services;

/// <summary>
/// 手动刷新与写后刷新的串行协调器：同一时刻只跑一次刷新，重入请求直接丢弃。
/// 不做任何定时轮询——界面只在用户点「刷新」、写操作落地或配置变更后重新读库。
/// </summary>
public sealed class RefreshCoordinator : IAsyncDisposable
{
    private readonly Func<CancellationToken, Task> _refresh;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public RefreshCoordinator(Func<CancellationToken, Task> refresh, IClock? clock = null)
    {
        _refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
        _clock = clock ?? SystemClock.Instance;
    }

    public event EventHandler? Refreshed;

    public DateTimeOffset? LastRefreshedAt { get; private set; }

    public int RefreshCount { get; private set; }

    public async Task RefreshNowAsync(CancellationToken cancellationToken)
    {
        if (!await _refreshLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            await _refresh(cancellationToken).ConfigureAwait(false);
            LastRefreshedAt = _clock.UtcNow;
            RefreshCount++;
            Refreshed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _refreshLock.Dispose();
        return ValueTask.CompletedTask;
    }
}
