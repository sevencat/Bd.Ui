using Bd.Core.BdCli;

namespace Bd.Core.Tests.Fakes;

/// <summary>按首个参数路由的假运行器，可统计并发与记录调用。</summary>
public sealed class FakeBdProcessRunner : IBdProcessRunner
{
    private readonly Dictionary<string, Func<BdProcessRequest, BdProcessResult>> _routes = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private readonly List<BdProcessRequest> _requests = [];
    private int _active;
    private int _maxActive;

    public Func<BdProcessRequest, BdProcessResult>? Fallback { get; set; }

    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    public int CallCount
    {
        get
        {
            lock (_sync)
            {
                return _requests.Count;
            }
        }
    }

    public int MaxConcurrency => Volatile.Read(ref _maxActive);

    public IReadOnlyList<BdProcessRequest> Requests
    {
        get
        {
            lock (_sync)
            {
                return [.. _requests];
            }
        }
    }

    public FakeBdProcessRunner Map(string command, Func<BdProcessRequest, BdProcessResult> handler)
    {
        _routes[command] = handler;
        return this;
    }

    public FakeBdProcessRunner MapJson(string command, string json) =>
        Map(command, _ => new BdProcessResult { ExitCode = 0, StdOut = json });

    public FakeBdProcessRunner MapFailure(string command, string stdErr, int exitCode = 1) =>
        Map(command, _ => new BdProcessResult { ExitCode = exitCode, StdErr = stdErr });

    public async Task<BdProcessResult> RunAsync(BdProcessRequest request, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            _requests.Add(request);
        }

        var active = Interlocked.Increment(ref _active);
        while (true)
        {
            var observed = Volatile.Read(ref _maxActive);
            if (active <= observed || Interlocked.CompareExchange(ref _maxActive, active, observed) == observed)
            {
                break;
            }
        }

        try
        {
            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();

            var command = request.Arguments.Count > 0 ? request.Arguments[0] : string.Empty;
            if (_routes.TryGetValue(command, out var handler))
            {
                return handler(request);
            }

            if (Fallback is not null)
            {
                return Fallback(request);
            }

            throw new InvalidOperationException($"Fake 未配置命令：{request.CommandText}");
        }
        finally
        {
            Interlocked.Decrement(ref _active);
        }
    }
}
