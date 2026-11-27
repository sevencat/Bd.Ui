using System.Data;
using System.Globalization;
using Bd.Core.Abstractions;
using Bd.Core.BdCli;
using Bd.Core.Domain;
using MySqlConnector;

namespace Bd.Core.Dolt;

/// <summary>
/// 直连远程 Dolt（MySQL 协议）的存储实现：一条 SQL 对应一次 bd 子进程调用，
/// 读路径完全不走 bd CLI，写路径见 <c>DoltStore.Write.cs</c>。
/// </summary>
public sealed partial class DoltStore : IBdStore
{
    public const string BackendNameValue = "dolt";

    /// <summary>IN (...) 单批最大参数个数，避免超过服务端预处理上限。</summary>
    internal const int ParameterBatchSize = 400;

    private readonly DoltConnectionFactory _factory;
    private readonly DoltConnectionSettings _settings;

    public DoltStore(DoltConnectionFactory factory, DoltConnectionSettings settings)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public string BackendName => BackendNameValue;

    public DoltConnectionSettings Settings => _settings;

    /// <summary>当前连接的数据库（一个 database 即一个 beads 项目）。</summary>
    public string Database => _settings.Database;

    private async Task<MySqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = _factory.CreateConnection(_settings);
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

    private static MySqlCommand CreateCommand(
        MySqlConnection connection,
        string sql,
        IReadOnlyList<MySqlParameter> parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 30;
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        return command;
    }

    /// <summary>把驱动异常翻译成带用户可读信息的 <see cref="BdException"/>。</summary>
    internal static BdException Translate(Exception exception) => exception switch
    {
        BdException bd => bd,
        MySqlException mySql => mySql.Number switch
        {
            1049 => new BdException(BdErrorCode.NotARepo, $"数据库不存在：{mySql.Message}", inner: mySql),
            1044 or 1045 => new BdException(BdErrorCode.InvalidArgument, $"Dolt 账号无权访问该库：{mySql.Message}", inner: mySql),
            2002 or 2003 or 2005 or 2006 => new BdException(BdErrorCode.Timeout, "无法连接 Dolt 服务器，请检查主机/端口/密码", inner: mySql),
            _ => new BdException(BdErrorCode.Unknown, $"Dolt 查询失败：{mySql.Message}", inner: mySql)
        },
        TimeoutException timeout => new BdException(BdErrorCode.Timeout, $"Dolt 连接超时：{timeout.Message}", inner: timeout),
        OperationCanceledException canceled => new BdException(BdErrorCode.Canceled, "操作已取消", inner: canceled),
        _ => new BdException(BdErrorCode.Unknown, exception.Message, inner: exception)
    };

    internal static Bead MapBead(IDataRecord row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new Bead
        {
            Id = ReadString(row, "id") ?? string.Empty,
            Title = ReadString(row, "title") ?? string.Empty,
            Description = ReadString(row, "description"),
            StatusRaw = ReadString(row, "status") ?? "open",
            PriorityValue = ReadInt32(row, "priority") ?? 2,
            TypeRaw = ReadString(row, "issue_type") ?? "task",
            Owner = ReadString(row, "owner"),
            CreatedBy = ReadString(row, "created_by"),
            CreatedAt = ReadTimestamp(row, "created_at"),
            UpdatedAt = ReadTimestamp(row, "updated_at"),
            ParentId = ReadString(row, "parent_id"),
            DependencyCount = ReadInt32(row, "dependency_count") ?? 0,
            DependentCount = ReadInt32(row, "dependent_count") ?? 0,
            CommentCount = ReadInt32(row, "comment_count") ?? 0
        };
    }

    internal static DependencyLink MapDependency(IDataRecord row) => new()
    {
        TargetId = ReadString(row, "target_id") ?? string.Empty,
        Title = ReadString(row, "target_title"),
        TargetStatusRaw = ReadString(row, "target_status"),
        TargetTypeRaw = ReadString(row, "target_type"),
        DependencyTypeRaw = ReadString(row, "dependency_type")
    };

    internal static Comment MapComment(IDataRecord row) => new()
    {
        Id = ReadString(row, "id") ?? string.Empty,
        IssueId = ReadString(row, "issue_id") ?? string.Empty,
        Author = ReadString(row, "author") ?? "unknown",
        Text = ReadString(row, "text") ?? string.Empty,
        CreatedAt = ReadTimestamp(row, "created_at")
    };

    private static string? ReadString(IDataRecord row, string column)
    {
        var ordinal = row.GetOrdinal(column);
        return row.IsDBNull(ordinal) ? null : row.GetValue(ordinal) as string ?? row.GetValue(ordinal).ToString();
    }

    private static int? ReadInt32(IDataRecord row, string column)
    {
        var ordinal = row.GetOrdinal(column);
        if (row.IsDBNull(ordinal))
        {
            return null;
        }

        var value = row.GetValue(ordinal);
        return value switch
        {
            int intValue => intValue,
            long longValue => (int)longValue,
            _ => Convert.ToInt32(value, CultureInfo.InvariantCulture)
        };
    }

    private static DateTimeOffset? ReadTimestamp(IDataRecord row, string column)
    {
        var ordinal = row.GetOrdinal(column);
        if (row.IsDBNull(ordinal))
        {
            return null;
        }

        return row.GetValue(ordinal) switch
        {
            DateTimeOffset offset => offset,
            DateTime dateTime => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
            string text when DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) => parsed,
            _ => null
        };
    }
}
