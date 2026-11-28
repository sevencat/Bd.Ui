namespace Bd.Core.Config;

public sealed record AppConfig
{
    public string? HumanActor { get; init; }

    public IReadOnlyList<string> HumanAllowlist { get; init; } = [];

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

    /// <summary>
    /// 连接密码（明文存于 config.json）；非空时优先于环境变量
    /// <c>BEADS_DOLT_PASSWORD</c>，显式留空则回退到环境变量。
    /// </summary>
    public string? DoltPassword { get; init; } = DoltDefaults.Password;

    /// <summary>当前项目对应的数据库名（一个 database 即一个项目）。</summary>
    public string DoltDatabase { get; init; } = DoltDefaults.Database;

    /// <summary>Dolt sql-server 默认不启用 TLS，直连时按需打开。</summary>
    public bool DoltUseTls { get; init; }

    /// <summary>选择「全部项目」：把服务器上所有库合并成一块看板（只读）。</summary>
    public bool DoltAggregateAll { get; init; }

    public static AppConfig Default { get; } = new();

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
        LogMaxFileSizeKb = Math.Clamp(LogMaxFileSizeKb, 64, 102_400),
        LogRetentionDays = Math.Clamp(LogRetentionDays, 1, 365),
        Theme = string.IsNullOrWhiteSpace(Theme) ? "Dark" : Theme,
        DoltHost = string.IsNullOrWhiteSpace(DoltHost) ? DoltDefaults.Host : DoltHost.Trim(),
        DoltPort = Math.Clamp(DoltPort, 1, 65_535),
        DoltUser = string.IsNullOrWhiteSpace(DoltUser) ? DoltDefaults.User : DoltUser.Trim(),
        DoltPassword = NormalizePassword(DoltPassword),
        DoltDatabase = string.IsNullOrWhiteSpace(DoltDatabase) ? DoltDefaults.Database : DoltDatabase.Trim(),
        HumanAllowlist = [.. HumanAllowlist
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)]
    };

    /// <summary>
    /// 字段缺失（null）时回退默认口令；显式留空表示“改用环境变量”，因此保留空串。
    /// </summary>
    private static string NormalizePassword(string? password) =>
        password is null ? DoltDefaults.Password : password.Trim();
}

/// <summary>团队共享 Dolt 服务器的默认连接参数；密码可被 config.json 或环境变量覆盖。</summary>
public static class DoltDefaults
{
    public const string Host = "1.15.245.172";

    public const int Port = 7050;

    public const string User = "nwms";

    public const string Password = "123478@!@56";

    public const string Database = "king_bdui";
}
