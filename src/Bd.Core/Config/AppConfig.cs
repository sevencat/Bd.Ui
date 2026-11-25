using System.Text.Json.Serialization;

namespace Bd.Core.Config;

public sealed record AppConfig
{
    public string? RepoPath { get; init; }

    public string BdBinaryPath { get; init; } = "bd";

    public string? HumanActor { get; init; }

    public IReadOnlyList<string> HumanAllowlist { get; init; } = [];

    public int PollSeconds { get; init; } = 5;

    public string Theme { get; init; } = "Dark";

    public bool ReadOnly { get; init; }

    /// <summary>显式使用内置演示数据，不启动 bd 子进程。</summary>
    public bool DemoMode { get; init; }

    /// <summary>单个日志文件的滚动上限（KB）。</summary>
    public int LogMaxFileSizeKb { get; init; } = 2048;

    /// <summary>滚动日志的保留天数。</summary>
    public int LogRetentionDays { get; init; } = 7;

    public string StorageBackend { get; init; } = "cli";

    public static AppConfig Default { get; } = new();

    [JsonIgnore]
    public TimeSpan PollInterval => TimeSpan.FromSeconds(Math.Clamp(PollSeconds, 1, 300));

    public bool IsHuman(string? actor, IReadOnlyList<string> extraAllowlist)
    {
        if (string.IsNullOrWhiteSpace(actor))
        {
            return false;
        }

        var candidate = actor.Trim();
        if (!string.IsNullOrWhiteSpace(HumanActor)
            && string.Equals(candidate, HumanActor.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return HumanAllowlist
            .Concat(extraAllowlist)
            .Any(entry => string.Equals(entry?.Trim(), candidate, StringComparison.OrdinalIgnoreCase));
    }

    public AppConfig Normalize() => this with
    {
        BdBinaryPath = string.IsNullOrWhiteSpace(BdBinaryPath) ? "bd" : BdBinaryPath.Trim(),
        PollSeconds = Math.Clamp(PollSeconds, 1, 300),
        LogMaxFileSizeKb = Math.Clamp(LogMaxFileSizeKb, 64, 102_400),
        LogRetentionDays = Math.Clamp(LogRetentionDays, 1, 365),
        Theme = string.IsNullOrWhiteSpace(Theme) ? "Dark" : Theme,
        StorageBackend = NormalizeStorageBackend(StorageBackend),
        HumanAllowlist = [.. HumanAllowlist
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)]
    };

    /// <summary>当前只支持 bd CLI；demo 是内置演示数据的等价入口，其余值一律回退为 cli。</summary>
    private static string NormalizeStorageBackend(string? backend) =>
        string.Equals(backend?.Trim(), "demo", StringComparison.OrdinalIgnoreCase) ? "demo" : "cli";
}
