using Bd.Core.Abstractions;

namespace Bd.Core.BdCli;

/// <summary>经 bd CLI 子进程读写 beads，所有调用集中在这里，避免出现第二个 bd 调用点。</summary>
public sealed partial class BdCliStore : IBdStore
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);
    private readonly IBdProcessRunner _runner;
    private readonly BdWriteGate _gate;
    private readonly string _workingDirectory;
    private readonly string? _actor;

    public BdCliStore(
        IBdProcessRunner runner,
        string workingDirectory,
        string? actor = null,
        BdWriteGate? gate = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _workingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
            ? throw new ArgumentException("仓库路径不能为空", nameof(workingDirectory))
            : workingDirectory;
        _actor = string.IsNullOrWhiteSpace(actor) ? null : actor.Trim();
        _gate = gate ?? new BdWriteGate();
    }

    public string BackendName => "bd-cli";

    public BdWriteGate WriteGate => _gate;

    private Task<BdProcessResult> RunReadAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        RunCoreAsync(arguments, cancellationToken);

    private Task<BdProcessResult> RunWriteAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        _gate.RunAsync(() => RunCoreAsync(arguments, cancellationToken), cancellationToken);

    private async Task<BdProcessResult> RunCoreAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var request = new BdProcessRequest
        {
            Arguments = arguments,
            WorkingDirectory = _workingDirectory,
            Actor = _actor,
            Timeout = DefaultTimeout
        };

        var result = await _runner.RunAsync(request, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw BdException.FromResult(result, request.CommandText, _actor);
        }

        return result;
    }

    private static string[] Build(string command, params string?[] parts)
    {
        var arguments = new List<string>(parts.Length + 1) { command };
        foreach (var part in parts)
        {
            if (!string.IsNullOrWhiteSpace(part))
            {
                arguments.Add(part);
            }
        }

        return [.. arguments];
    }
}
