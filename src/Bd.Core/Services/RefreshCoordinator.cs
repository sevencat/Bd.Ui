using Bd.Core.Abstractions;

namespace Bd.Core.Services;

/// <summary>轮询 + 写后立即失效的节流协调器，所有刷新串行执行且可取消。</summary>
public sealed class RefreshCoordinator : IAsyncDisposable
{
    private readonly Func<CancellationToken, Task> _refresh;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private CancellationTokenSource? _loopSource;
    private Task? _loop;
    private int _invalidated;

    public RefreshCoordinator(Func<CancellationToken, Task> refresh, IClock? clock = null, TimeSpan? interval = null)
    {
        _refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
        _clock = clock ?? SystemClock.Instance;
        Interval = Normalize(interval ?? TimeSpan.FromSeconds(5));
    }

    public event EventHandler? Refreshed;

    public event EventHandler<Exception>? RefreshFailed;

    public TimeSpan Interval { get; private set; }

    public DateTimeOffset? LastRefreshedAt { get; private set; }

    public int RefreshCount { get; private set; }

    public bool IsRunning => _loop is { IsCompleted: false };

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        _loopSource?.Dispose();
        _loopSource = new CancellationTokenSource();
        _loop = LoopAsync(_loopSource.Token);
    }

    public async Task StopAsync()
    {
        if (_loopSource is null)
        {
            return;
        }

        await _loopSource.CancelAsync().ConfigureAwait(false);
        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 正常停止。
            }
        }

        _loopSource.Dispose();
        _loopSource = null;
        _loop = null;
    }

    public void SetInterval(TimeSpan interval)
    {
        Interval = Normalize(interval);
        if (IsRunning)
        {
            _ = StopAsync().ContinueWith(_ => Start(), TaskScheduler.Default);
        }
    }

    /// <summary>写操作后调用，下一次循环立即刷新。</summary>
    public void Invalidate() => Interlocked.Exchange(ref _invalidated, 1);

    public async Task RefreshNowAsync(CancellationToken cancellationToken)
    {
        if (!await _refreshLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            Invalidate();
            return;
        }

        try
        {
            Interlocked.Exchange(ref _invalidated, 0);
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

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _refreshLock.Dispose();
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _clock.DelayAsync(Interval, cancellationToken).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                await RefreshNowAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                RefreshFailed?.Invoke(this, ex);
            }
        }
    }

    private static TimeSpan Normalize(TimeSpan interval) =>
        interval < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : interval;
}
