using System.Text;
using Bd.Core.Abstractions;
using MySqlConnector;

namespace Bd.Core.Dolt;

/// <summary>
/// DoltStore 的 SQL 拼装。所有值一律走参数，动态部分只允许列名/占位符，
/// 便于在不连库的情况下用单测覆盖筛选与转义逻辑。
/// </summary>
internal static class DoltSql
{
    /// <summary>
    /// issues 行 → Bead 的公共投影。计数口径与 <c>bd list --json</c> 对齐：
    /// dependency_count / dependent_count 不统计 parent-child（父任务不算依赖），
    /// parent 只取 parent-child 指向的一端。
    /// </summary>
    public const string BeadProjection = """
        i.id, i.title, i.description, i.status, i.priority, i.issue_type,
        i.owner, i.created_by, i.created_at, i.updated_at,
        (SELECT COUNT(*) FROM dependencies d
           WHERE d.issue_id = i.id AND d.type <> 'parent-child') AS dependency_count,
        (SELECT COUNT(*) FROM dependencies d
           WHERE d.depends_on_issue_id = i.id AND d.type <> 'parent-child') AS dependent_count,
        (SELECT COUNT(*) FROM comments c WHERE c.issue_id = i.id) AS comment_count,
        (SELECT d.depends_on_issue_id FROM dependencies d
           WHERE d.issue_id = i.id AND d.type = 'parent-child'
           ORDER BY d.created_at LIMIT 1) AS parent_id
        """;

    /// <summary>依赖边投影：target 可能指向 issues / wisps / 外部 ID。</summary>
    public const string DependencyProjection = """
        d.issue_id,
        COALESCE(d.depends_on_issue_id, d.depends_on_external, d.depends_on_wisp_id) AS target_id,
        d.type AS dependency_type,
        t.title AS target_title,
        t.status AS target_status,
        t.issue_type AS target_type
        """;

    public const string CommentProjection = "c.id, c.issue_id, c.author, c.text, c.created_at";

    private const string OrderByClause = " ORDER BY i.priority, i.updated_at DESC, i.id";

    public static (string Sql, IReadOnlyList<MySqlParameter> Parameters) BuildListQuery(BeadListQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var conditions = new List<string>();
        var parameters = new List<MySqlParameter>();

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            conditions.Add("i.status = @status");
            parameters.Add(new MySqlParameter("@status", query.Status.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(query.Type))
        {
            conditions.Add("i.issue_type = @type");
            parameters.Add(new MySqlParameter("@type", query.Type.Trim()));
        }

        // bd 的 -p 只接受数字优先级，非数字时保持与 CLI 一致：忽略该筛选。
        if (!string.IsNullOrWhiteSpace(query.Priority)
            && int.TryParse(query.Priority.Trim(), out var priority))
        {
            conditions.Add("i.priority = @priority");
            parameters.Add(new MySqlParameter("@priority", priority));
        }

        if (!string.IsNullOrWhiteSpace(query.Label))
        {
            conditions.Add("EXISTS (SELECT 1 FROM labels l WHERE l.issue_id = i.id AND l.label = @label)");
            parameters.Add(new MySqlParameter("@label", query.Label.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            conditions.Add(
                "(i.id LIKE @search ESCAPE '!' OR i.title LIKE @search ESCAPE '!' OR i.description LIKE @search ESCAPE '!')");
            parameters.Add(new MySqlParameter("@search", ToLikePattern(query.Search)));
        }

        var sql = new StringBuilder("SELECT ").Append(BeadProjection).Append(" FROM issues i");
        if (conditions.Count > 0)
        {
            sql.Append(" WHERE ").Append(string.Join(" AND ", conditions));
        }

        sql.Append(OrderByClause);
        if (query.Limit > 0)
        {
            sql.Append(" LIMIT @limit");
            parameters.Add(new MySqlParameter("@limit", query.Limit));
        }

        return (sql.ToString(), parameters);
    }

    /// <summary>ready 视图已排除阻塞、defer 与封存状态，这里只需按看板顺序取行。</summary>
    public static (string Sql, IReadOnlyList<MySqlParameter> Parameters) BuildReadyQuery() =>
        ($"SELECT {BeadProjection} FROM ready_issues i{OrderByClause}", []);

    /// <summary>blocked 视图已按 blocks 边与状态过滤，再排除已关闭/固定以贴合 bd blocked。</summary>
    public static (string Sql, IReadOnlyList<MySqlParameter> Parameters) BuildBlockedQuery() =>
        ($"SELECT {BeadProjection} FROM blocked_issues i WHERE i.status NOT IN ('closed', 'pinned'){OrderByClause}", []);

    public static (string Sql, IReadOnlyList<MySqlParameter> Parameters) BuildGetQuery(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return (
            $"SELECT {BeadProjection} FROM issues i WHERE i.id = @id LIMIT 1",
            [new MySqlParameter("@id", id.Trim())]);
    }

    public static (string Sql, IReadOnlyList<MySqlParameter> Parameters) BuildLabelQuery(IReadOnlyList<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var parameters = new List<MySqlParameter>(ids.Count);
        var placeholders = BuildPlaceholders(ids, "@l", parameters);
        return ($"SELECT l.issue_id, l.label FROM labels l WHERE l.issue_id IN ({placeholders}) ORDER BY l.issue_id, l.label", parameters);
    }

    public static (string Sql, IReadOnlyList<MySqlParameter> Parameters) BuildDependencyQuery(IReadOnlyList<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var parameters = new List<MySqlParameter>(ids.Count);
        var placeholders = BuildPlaceholders(ids, "@d", parameters);
        return (
            $"""
             SELECT {DependencyProjection}
             FROM dependencies d
             LEFT JOIN issues t ON t.id = d.depends_on_issue_id
             WHERE d.issue_id IN ({placeholders})
             ORDER BY d.issue_id, d.created_at
             """,
            parameters);
    }

    public static (string Sql, IReadOnlyList<MySqlParameter> Parameters) BuildCommentQuery(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return (
            $"SELECT {CommentProjection} FROM comments c WHERE c.issue_id = @id ORDER BY c.created_at, c.id",
            [new MySqlParameter("@id", id.Trim())]);
    }

    public const string VersionQuery =
        "SELECT `value` FROM local_metadata WHERE `key` = 'bd_version' LIMIT 1";

    public const string SchemaVersionQuery = "SELECT MAX(version) AS v FROM schema_migrations";

    public const string IssueCountQuery = "SELECT COUNT(*) AS v FROM issues";

    /// <summary>LIKE 通配符转义，转义符固定为 <c>!</c>，避免与反斜杠转义规则纠缠。</summary>
    public static string ToLikePattern(string keyword)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);
        var escaped = keyword
            .Trim()
            .Replace("!", "!!", StringComparison.Ordinal)
            .Replace("%", "!%", StringComparison.Ordinal)
            .Replace("_", "!_", StringComparison.Ordinal);
        return $"%{escaped}%";
    }

    private static string BuildPlaceholders(
        IReadOnlyList<string> ids,
        string prefix,
        List<MySqlParameter> parameters)
    {
        var placeholders = new string[ids.Count];
        for (var index = 0; index < ids.Count; index++)
        {
            var name = $"{prefix}{index}";
            placeholders[index] = name;
            parameters.Add(new MySqlParameter(name, ids[index]));
        }

        return string.Join(", ", placeholders);
    }
}
