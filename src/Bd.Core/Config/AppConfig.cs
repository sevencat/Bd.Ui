using System.Text.Json.Serialization;

namespace Bd.Core.Config;

public sealed record AppConfig
{
    public string? HumanActor { get; init; }

    public IReadOnlyList<string> HumanAllowlist { get; init; } = [];

    public int PollSeconds { get; init; } = 5;

    public string Theme { get; init; } = "Dark";

    public bool ReadOnly { get; init; }

    /// <summary>显式使用内置演示数据，不连接 Dolt。</summary>
    public bool DemoMode { get; init; }

    /// <summary>单个日志文件的滚动上限（KB）。</summary>
    public int LogMaxFileSizeKb { get; init; } = 2048;

    /// <summary>滚动日志的保留天数。</summary>
    public int LogRetentionDays { get; init; } = 7;

    /// <summary>远程 Dolt 服务器主机（MySQL 协议）。</summary>
    public string DoltHost { get; init; } = DoltDefaults.Host;

    public int DoltPort { get; init; } = DoltDefaults.Port;

    public string DoltUser { get; init; } = DoltDefaults.User;

    /// <summary>当前项目对应的数据库名（一个 database 即一个项目）。</summary>
    public string DoltDatabase { get; init; } = DoltDefaults.Database;

    /// <summary>Dolt sql-server 默认不启用 TLS，直连时按需打开。</summary>
    public bool DoltUseTls { get; init; }

    /// <summary>选择「全部项目」：把服务器上所有库合并成一块看板（只读）。</summary>
    public bool DoltAggregateAll { get; init; }

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
        PollSeconds = Math.Clamp(PollSeconds, 1, 300),
        LogMaxFileSizeKb = Math.Clamp(LogMaxFileSizeKb, 64, 102_400),
        LogRetentionDays = Math.Clamp(LogRetentionDays, 1, 365),
        Theme = string.IsNullOrWhiteSpace(Theme) ? "Dark" : Theme,
        DoltHost = string.IsNullOrWhiteSpace(DoltHost) ? DoltDefaults.Host : DoltHost.Trim(),
        DoltPort = Math.Clamp(DoltPort, 1, 65_535),
        DoltUser = string.IsNullOrWhiteSpace(DoltUser) ? DoltDefaults.User : DoltUser.Trim(),
        DoltDatabase = string.IsNullOrWhiteSpace(DoltDatabase) ? DoltDefaults.Database : DoltDatabase.Trim(),
        HumanAllowlist = [.. HumanAllowlist
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)]
    };
}

/// <summary>团队共享 Dolt 服务器的默认连接参数；密码只从环境变量读取。</summary>
public static class DoltDefaults
{
    public const string Host = "1.15.245.172";

    public const int Port = 7050;

    public const string User = "nwms";

    public const string Database = "king_bdui";
}
