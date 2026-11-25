using System.Text.Json;
using Bd.Core.Abstractions;
using Bd.Core.Domain;

namespace Bd.Core.BdCli;

public sealed partial class BdCliStore
{
    public async Task<IReadOnlyList<Bead>> ListAsync(BeadListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var arguments = new List<string> { "list", "--json", "-n", (query.Limit > 0 ? query.Limit : 0).ToString() };
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            arguments.Add("-s");
            arguments.Add(query.Status);
        }

        if (!string.IsNullOrWhiteSpace(query.Type))
        {
            arguments.Add("-t");
            arguments.Add(query.Type);
        }

        if (!string.IsNullOrWhiteSpace(query.Priority))
        {
            arguments.Add("-p");
            arguments.Add(query.Priority);
        }

        if (!string.IsNullOrWhiteSpace(query.Label))
        {
            arguments.Add("-l");
            arguments.Add(query.Label);
        }

        var result = await RunReadAsync(arguments, cancellationToken).ConfigureAwait(false);
        var beads = BdJson.ParseArray<Bead>(result.StdOut, out _);

        if (string.IsNullOrWhiteSpace(query.Search))
        {
            return beads;
        }

        var keyword = query.Search.Trim();
        return
        [
            .. beads.Where(bead =>
                bead.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || bead.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || (bead.Description?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false))
        ];
    }

    public async Task<IReadOnlyList<Bead>> ReadyAsync(CancellationToken cancellationToken)
    {
        var result = await RunReadAsync(["ready", "--json"], cancellationToken).ConfigureAwait(false);
        return BdJson.ParseArray<Bead>(result.StdOut, out _);
    }

    public async Task<IReadOnlyList<Bead>> BlockedAsync(CancellationToken cancellationToken)
    {
        var result = await RunReadAsync(["blocked", "--json"], cancellationToken).ConfigureAwait(false);
        var beads = BdJson.ParseArray<Bead>(result.StdOut, out _);
        return [.. beads.Where(static bead => !bead.IsClosed)];
    }

    public async Task<Bead> GetAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var result = await RunReadAsync(["show", id, "--json"], cancellationToken).ConfigureAwait(false);
        var bead = BdJson.ParseSingleOrDefault<Bead>(result.StdOut, out _);
        return bead ?? throw BdException.NotFound(id);
    }

    public async Task<IReadOnlyList<DependencyLink>> ListDependenciesAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var result = await RunReadAsync(["dep", "list", id, "--json"], cancellationToken).ConfigureAwait(false);
        return BdJson.ParseArray<DependencyLink>(result.StdOut, out _);
    }

    public async Task<IReadOnlyList<Comment>> ListCommentsAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var result = await RunReadAsync(["comments", id, "--json"], cancellationToken).ConfigureAwait(false);
        return ParseComments(result.StdOut, id);
    }

    public async Task<string> GetVersionAsync(CancellationToken cancellationToken)
    {
        var result = await RunReadAsync(["version"], cancellationToken).ConfigureAwait(false);
        return ParseVersion(result.StdOut);
    }

    public async Task<DoctorReport> RunDoctorAsync(CancellationToken cancellationToken)
    {
        string version;
        try
        {
            version = await GetVersionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (BdException ex)
        {
            return new DoctorReport
            {
                Ok = false,
                RepoPath = _workingDirectory,
                Summary = ex.UserMessage,
                Issues = [ex.UserMessage]
            };
        }

        try
        {
            var result = await RunReadAsync(["doctor", "--json"], cancellationToken).ConfigureAwait(false);
            var issues = ExtractDoctorIssues(result.StdOut);
            return new DoctorReport
            {
                Ok = issues.Count == 0,
                BdVersion = version,
                RepoPath = _workingDirectory,
                SchemaVersion = BdJson.ReadSchemaVersion(result.StdOut),
                Summary = issues.Count == 0 ? "环境检查通过" : $"发现 {issues.Count} 项问题",
                RawOutput = result.StdOut,
                Issues = issues
            };
        }
        catch (BdException ex)
        {
            return new DoctorReport
            {
                Ok = false,
                BdVersion = version,
                RepoPath = _workingDirectory,
                Summary = ex.UserMessage,
                RawOutput = ex.StdErr ?? string.Empty,
                Issues = [ex.UserMessage]
            };
        }
    }

    private static string ParseVersion(string stdout)
    {
        var text = stdout.Trim();
        var marker = text.IndexOf("bd version ", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return text.Split('\n', StringSplitOptions.TrimEntries).FirstOrDefault() ?? text;
        }

        var rest = text[(marker + "bd version ".Length)..];
        var end = rest.IndexOfAny([' ', '\r', '\n']);
        return end < 0 ? rest : rest[..end];
    }

    /// <summary>bd comments 的字段名在不同版本间略有差异，这里按候选键宽容读取。</summary>
    private static IReadOnlyList<Comment> ParseComments(string stdout, string issueId)
    {
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return [];
        }

        using var document = JsonDocument.Parse(stdout);
        var root = document.RootElement;
        var data = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var inner) ? inner : root;
        if (data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var comments = new List<Comment>();
        var index = 0;
        foreach (var item in data.EnumerateArray())
        {
            index++;
            comments.Add(new Comment
            {
                Id = ReadString(item, "id") ?? $"comment-{index}",
                IssueId = ReadString(item, "issue_id", "bead_id") ?? issueId,
                Author = ReadString(item, "author", "created_by", "actor") ?? "unknown",
                Text = ReadString(item, "text", "body", "content", "comment") ?? string.Empty,
                CreatedAt = ReadTimestamp(item, "created_at", "timestamp")
            });
        }

        return [.. comments.OrderBy(static comment => comment.CreatedAt ?? DateTimeOffset.MinValue)];
    }

    private static string? ReadString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static DateTimeOffset? ReadTimestamp(JsonElement element, params string[] names)
    {
        var raw = ReadString(element, names);
        return DateTimeOffset.TryParse(raw, out var value) ? value : null;
    }

    private static List<string> ExtractDoctorIssues(string stdout)
    {
        var issues = new List<string>();
        if (!BdJson.TryReadError(stdout, out var payload))
        {
            return issues;
        }

        if (payload!.Error is { Length: > 0 } error)
        {
            issues.Add(error);
        }

        if (payload.Hint is { Length: > 0 } hint)
        {
            issues.Add(hint);
        }

        return issues;
    }
}
