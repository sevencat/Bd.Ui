using System.Text;
using MySqlConnector;

namespace Bd.Core.Dolt;

/// <summary>服务器上的一个 beads 项目（一个 database 即一个项目）。</summary>
public sealed record ProjectInfo(string Database, string? Prefix, int BeadCount, string? BdVersion, bool IsAggregate = false)
{
    /// <summary>「全部项目」聚合项的占位库名。</summary>
    public const string AggregateDatabase = CompositeBdStore.AggregateDatabase;

    /// <summary>下拉里的「全部项目」条目，条数为各项目之和。</summary>
    public static ProjectInfo Aggregate(int beadCount) => new(AggregateDatabase, null, beadCount, null, IsAggregate: true);

    /// <summary>下拉框展示文本，例如 <c>Bd_Ui · king_bdui (6)</c>。</summary>
    public string Label => IsAggregate
        ? $"全部项目 ({BeadCount})"
        : string.IsNullOrWhiteSpace(Prefix) ? $"{Database} ({BeadCount})" : $"{Prefix} · {Database} ({BeadCount})";
}

/// <summary>
/// 枚举远程 Dolt 服务器上的全部 beads 项目：只认「有 issues 表」的库，
/// 前缀取 <c>config.issue_prefix</c>，版本取 <c>local_metadata.bd_version</c>。
/// </summary>
public sealed class ProjectCatalog
{
    private const string DatabasesSql = """
        SELECT TABLE_SCHEMA
        FROM information_schema.TABLES
        WHERE TABLE_NAME = 'issues'
          AND TABLE_TYPE = 'BASE TABLE'
          AND TABLE_SCHEMA NOT IN ('information_schema', 'mysql', 'performance_schema', 'sys')
        ORDER BY TABLE_SCHEMA
        """;

    private readonly DoltConnectionFactory _factory;

    public ProjectCatalog(DoltConnectionFactory factory) =>
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    public async Task<IReadOnlyList<ProjectInfo>> ListAsync(
        DoltConnectionSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            var databases = await ListDatabasesAsync(settings, cancellationToken).ConfigureAwait(false);
            if (databases.Count == 0)
            {
                return [];
            }

            var projects = await ReadProjectsAsync(settings, databases, cancellationToken).ConfigureAwait(false);
            return [.. projects
                .Where(static project => project is not null)
                .Select(static project => project!)
                .OrderByDescending(static project => project.BeadCount)
                .ThenBy(static project => project.Database, StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception exception)
        {
            throw DoltStore.Translate(exception);
        }
    }

    /// <summary>枚举包含 issues 表的数据库名（元数据查询，连接不绑定具体库）。</summary>
    internal static string DatabasesQuery => DatabasesSql;

    /// <summary>单条 UNION ALL 语句同时取回多个库的条数/前缀/版本，避免 N+1 往返。</summary>
    internal static string BuildProjectsSql(IReadOnlyList<string> databases)
    {
        ArgumentNullException.ThrowIfNull(databases);

        var builder = new StringBuilder();
        for (var index = 0; index < databases.Count; index++)
        {
            if (index > 0)
            {
                builder.AppendLine("UNION ALL");
            }

            var identifier = QuoteIdentifier(databases[index]);
            builder.AppendLine(
                $"SELECT @p{index} AS database_name, " +
                $"(SELECT COUNT(*) FROM {identifier}.`issues`) AS bead_count, " +
                $"(SELECT `value` FROM {identifier}.`config` WHERE `key` = 'issue_prefix' LIMIT 1) AS issue_prefix, " +
                $"(SELECT `value` FROM {identifier}.`local_metadata` WHERE `key` = 'bd_version' LIMIT 1) AS bd_version");
        }

        return builder.ToString();
    }

    /// <summary>反引号标识符转义：库名来自服务端元数据，仍然按 MySQL 规则双写反引号。</summary>
    internal static string QuoteIdentifier(string name) =>
        "`" + (name ?? throw new ArgumentNullException(nameof(name))).Replace("`", "``", StringComparison.Ordinal) + "`";

    internal static ProjectInfo MapProject(System.Data.IDataRecord row)
    {
        ArgumentNullException.ThrowIfNull(row);

        var database = row.IsDBNull(0) ? string.Empty : Convert.ToString(row.GetValue(0), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        var count = row.IsDBNull(1) ? 0 : Convert.ToInt32(row.GetValue(1), System.Globalization.CultureInfo.InvariantCulture);
        var prefix = row.IsDBNull(2) ? null : Convert.ToString(row.GetValue(2), System.Globalization.CultureInfo.InvariantCulture);
        var version = row.IsDBNull(3) ? null : Convert.ToString(row.GetValue(3), System.Globalization.CultureInfo.InvariantCulture);

        return new ProjectInfo(database, prefix, count, version);
    }

    private async Task<IReadOnlyList<string>> ListDatabasesAsync(
        DoltConnectionSettings settings,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(settings, cancellationToken).ConfigureAwait(false);
        await using var command = CreateCommand(connection, DatabasesSql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var databases = new List<string>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!reader.IsDBNull(0))
            {
                databases.Add(reader.GetString(0));
            }
        }

        return databases;
    }

    private async Task<IReadOnlyList<ProjectInfo?>> ReadProjectsAsync(
        DoltConnectionSettings settings,
        IReadOnlyList<string> databases,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(settings, cancellationToken).ConfigureAwait(false);

        try
        {
            await using var command = CreateCommand(connection, BuildProjectsSql(databases));
            for (var index = 0; index < databases.Count; index++)
            {
                command.Parameters.AddWithValue($"@p{index}", databases[index]);
            }

            var projects = new List<ProjectInfo?>(databases.Count);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                projects.Add(MapProject(reader));
            }

            return projects;
        }
        catch (MySqlException)
        {
            // 个别库可能缺 config/local_metadata（半成品库），退化为逐库读取，坏的跳过。
            return await ReadProjectsOneByOneAsync(connection, databases, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<IReadOnlyList<ProjectInfo?>> ReadProjectsOneByOneAsync(
        MySqlConnection connection,
        IReadOnlyList<string> databases,
        CancellationToken cancellationToken)
    {
        var projects = new List<ProjectInfo?>(databases.Count);
        foreach (var database in databases)
        {
            try
            {
                await using var command = CreateCommand(connection, BuildProjectsSql([database]));
                command.Parameters.AddWithValue("@p0", database);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    projects.Add(MapProject(reader));
                }
            }
            catch (MySqlException exception) when (exception.Number is 1146 or 1049 or 1044)
            {
                // 缺表 / 库被删 / 无权限：该库不算项目。
            }
        }

        return projects;
    }

    private async Task<MySqlConnection> OpenAsync(DoltConnectionSettings settings, CancellationToken cancellationToken)
    {
        // 元数据查询跨库，连接本身不绑定任何 database，避免当前项目被删时整个目录为空。
        var connection = _factory.CreateConnection(settings with { Database = string.Empty });
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static MySqlCommand CreateCommand(MySqlConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 30;
        return command;
    }
}
