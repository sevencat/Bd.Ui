using Bd.Core.Config;

namespace Bd.Core.Dolt;

/// <summary>
/// 远程 Dolt（MySQL 协议）连接参数。密码不在这里，运行时由
/// <see cref="DoltConnectionFactory"/> 从环境变量读取，避免落盘。
/// </summary>
public sealed record DoltConnectionSettings
{
    public required string Host { get; init; }

    public int Port { get; init; } = DoltDefaults.Port;

    public required string User { get; init; }

    /// <summary>项目对应的数据库名（一个 database 即一个 beads 项目）。</summary>
    public required string Database { get; init; }

    public bool UseTls { get; init; }

    /// <summary>写操作署名（created_by / events.actor / lease.holder）；为空时从库里最近一次 created_by 推断。</summary>
    public string? Actor { get; init; }

    public static DoltConnectionSettings FromConfig(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new DoltConnectionSettings
        {
            Host = config.DoltHost,
            Port = config.DoltPort,
            User = config.DoltUser,
            Database = config.DoltDatabase,
            UseTls = config.DoltUseTls,
            Actor = string.IsNullOrWhiteSpace(config.HumanActor) ? null : config.HumanActor.Trim()
        };
    }

    public DoltConnectionSettings WithDatabase(string database)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        return this with { Database = database.Trim() };
    }

    public string Describe() => $"{User}@{Host}:{Port}/{Database}";
}
