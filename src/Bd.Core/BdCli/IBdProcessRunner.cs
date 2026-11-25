namespace Bd.Core.BdCli;

public sealed record BdProcessRequest
{
    public required IReadOnlyList<string> Arguments { get; init; }

    public required string WorkingDirectory { get; init; }

    public string? Actor { get; init; }

    public TimeSpan? Timeout { get; init; }

    public string CommandText => string.Join(' ', Arguments);
}

public sealed record BdProcessResult
{
    public required int ExitCode { get; init; }

    public string StdOut { get; init; } = string.Empty;

    public string StdErr { get; init; } = string.Empty;

    public bool TimedOut { get; init; }

    public bool Succeeded => ExitCode == 0 && !TimedOut;
}

public interface IBdProcessRunner
{
    Task<BdProcessResult> RunAsync(BdProcessRequest request, CancellationToken cancellationToken);
}
