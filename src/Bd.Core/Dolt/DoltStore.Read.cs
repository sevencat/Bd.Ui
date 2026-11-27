using Bd.Core.Abstractions;
using Bd.Core.BdCli;
using Bd.Core.Domain;
using MySqlConnector;

namespace Bd.Core.Dolt;

public sealed partial class DoltStore
{
    public async Task<IReadOnlyList<Bead>> ListAsync(BeadListQuery query, CancellationToken cancellationToken)
    {
        var (sql, parameters) = DoltSql.BuildListQuery(query);
        return await QueryBeadsAsync(sql, parameters, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Bead>> ReadyAsync(CancellationToken cancellationToken)
    {
        var (sql, parameters) = DoltSql.BuildReadyQuery();
        return await QueryBeadsAsync(sql, parameters, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Bead>> BlockedAsync(CancellationToken cancellationToken)
    {
        var (sql, parameters) = DoltSql.BuildBlockedQuery();
        return await QueryBeadsAsync(sql, parameters, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Bead> GetAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var (sql, parameters) = DoltSql.BuildGetQuery(id);
        var beads = await QueryBeadsAsync(sql, parameters, cancellationToken).ConfigureAwait(false);
        return beads.Count == 0 ? throw BdException.NotFound(id) : beads[0];
    }

    public async Task<IReadOnlyList<DependencyLink>> ListDependenciesAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            var map = await LoadDependenciesAsync(connection, [id.Trim()], cancellationToken).ConfigureAwait(false);
            return map.TryGetValue(id.Trim(), out var links) ? links : [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw Translate(ex);
        }
    }

    public async Task<IReadOnlyList<Comment>> ListCommentsAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var (sql, parameters) = DoltSql.BuildCommentQuery(id);
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = CreateCommand(connection, sql, parameters);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            var comments = new List<Comment>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                comments.Add(MapComment(reader));
            }

            return comments;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw Translate(ex);
        }
    }

    public async Task<string> GetVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = CreateCommand(connection, DoltSql.VersionQuery, []);
            var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return value as string ?? value?.ToString() ?? string.Empty;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw Translate(ex);
        }
    }

    public async Task<DoctorReport> RunDoctorAsync(CancellationToken cancellationToken)
    {
        var issues = new List<string>();
        var version = string.Empty;
        var schemaVersion = 0;
        var issueCount = 0;
        var repo = $"dolt://{_settings.Host}:{_settings.Port}/{_settings.Database}";

        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);

            await using (var command = CreateCommand(connection, DoltSql.VersionQuery, []))
            {
                version = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string ?? string.Empty;
            }

            await using (var command = CreateCommand(connection, DoltSql.SchemaVersionQuery, []))
            {
                var scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                schemaVersion = scalar is null or DBNull ? 0 : Convert.ToInt32(scalar, System.Globalization.CultureInfo.InvariantCulture);
            }

            await using (var command = CreateCommand(connection, DoltSql.IssueCountQuery, []))
            {
                var scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                issueCount = scalar is null or DBNull ? 0 : Convert.ToInt32(scalar, System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var translated = Translate(ex);
            return new DoctorReport
            {
                Ok = false,
                RepoPath = repo,
                Summary = translated.UserMessage,
                Issues = [translated.UserMessage]
            };
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            issues.Add("local_metadata 中缺少 bd_version，无法确认 schema 版本");
        }

        if (schemaVersion <= 0)
        {
            issues.Add("schema_migrations 为空，数据库可能尚未初始化");
        }

        return new DoctorReport
        {
            Ok = issues.Count == 0,
            BdVersion = version,
            RepoPath = repo,
            SchemaVersion = schemaVersion,
            Summary = issues.Count == 0
                ? $"直连正常：{version} / schema {schemaVersion} / {issueCount} 条 issue"
                : $"发现 {issues.Count} 项问题",
            RawOutput = $"database={_settings.Database} issues={issueCount}",
            Issues = issues
        };
    }

    private async Task<IReadOnlyList<Bead>> QueryBeadsAsync(
        string sql,
        IReadOnlyList<MySqlParameter> parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);

            var beads = new List<Bead>();
            await using (var command = CreateCommand(connection, sql, parameters))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    beads.Add(MapBead(reader));
                }
            }

            if (beads.Count == 0)
            {
                return beads;
            }

            var ids = beads.ConvertAll(static bead => bead.Id);
            var labels = await LoadLabelsAsync(connection, ids, cancellationToken).ConfigureAwait(false);
            var dependencies = await LoadDependenciesAsync(connection, ids, cancellationToken).ConfigureAwait(false);

            return
            [
                .. beads.Select(bead => bead with
                {
                    Labels = labels.TryGetValue(bead.Id, out var beadLabels) ? beadLabels : [],
                    Dependencies = dependencies.TryGetValue(bead.Id, out var beadDependencies) ? beadDependencies : []
                })
            ];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw Translate(ex);
        }
    }

    private async Task<Dictionary<string, IReadOnlyList<string>>> LoadLabelsAsync(
        MySqlConnection connection,
        IReadOnlyList<string> ids,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var batch in Chunk(ids))
        {
            var (sql, parameters) = DoltSql.BuildLabelQuery(batch);
            await using var command = CreateCommand(connection, sql, parameters);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var issueId = ReadString(reader, "issue_id");
                var label = ReadString(reader, "label");
                if (issueId is null || label is null)
                {
                    continue;
                }

                if (!result.TryGetValue(issueId, out var list))
                {
                    list = [];
                    result[issueId] = list;
                }

                list.Add(label);
            }
        }

        return result.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyList<string>)[.. pair.Value],
            StringComparer.Ordinal);
    }

    private async Task<Dictionary<string, IReadOnlyList<DependencyLink>>> LoadDependenciesAsync(
        MySqlConnection connection,
        IReadOnlyList<string> ids,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, List<DependencyLink>>(StringComparer.Ordinal);

        foreach (var batch in Chunk(ids))
        {
            var (sql, parameters) = DoltSql.BuildDependencyQuery(batch);
            await using var command = CreateCommand(connection, sql, parameters);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var issueId = ReadString(reader, "issue_id");
                if (issueId is null)
                {
                    continue;
                }

                if (!result.TryGetValue(issueId, out var list))
                {
                    list = [];
                    result[issueId] = list;
                }

                list.Add(MapDependency(reader));
            }
        }

        return result.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyList<DependencyLink>)[.. pair.Value],
            StringComparer.Ordinal);
    }

    private static IEnumerable<IReadOnlyList<string>> Chunk(IReadOnlyList<string> ids)
    {
        for (var offset = 0; offset < ids.Count; offset += ParameterBatchSize)
        {
            var size = Math.Min(ParameterBatchSize, ids.Count - offset);
            var batch = new string[size];
            for (var index = 0; index < size; index++)
            {
                batch[index] = ids[offset + index];
            }

            yield return batch;
        }
    }
}
