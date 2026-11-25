using Bd.Core.Abstractions;
using Bd.Core.Domain;

namespace Bd.Core.BdCli;

public sealed partial class BdCliStore
{
    public async Task<string> CreateAsync(CreateBeadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new BdException(BdErrorCode.InvalidArgument, "任务标题不能为空");
        }

        var arguments = new List<string>
        {
            "create",
            request.Title.Trim(),
            "-t",
            request.Type,
            "-p",
            request.Priority.ToString()
        };

        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            arguments.Add("-d");
            arguments.Add(request.Description);
        }

        if (!string.IsNullOrWhiteSpace(request.Assignee))
        {
            arguments.Add("-a");
            arguments.Add(request.Assignee);
        }

        if (!string.IsNullOrWhiteSpace(request.ParentId))
        {
            arguments.Add("--parent");
            arguments.Add(request.ParentId);
        }

        foreach (var label in request.Labels.Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            arguments.Add("-l");
            arguments.Add(label.Trim());
        }

        arguments.Add("--silent");

        var result = await RunWriteAsync(arguments, cancellationToken).ConfigureAwait(false);
        var id = result.StdOut
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();

        if (string.IsNullOrWhiteSpace(id))
        {
            throw new BdException(BdErrorCode.Unknown, "bd create 未返回任务 ID", result.StdOut);
        }

        return id;
    }

    public async Task UpdateAsync(string id, UpdateBeadRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(request);
        if (!request.HasChanges)
        {
            return;
        }

        var arguments = new List<string> { "update", id };
        if (request.Title is { Length: > 0 } title)
        {
            arguments.Add("--title");
            arguments.Add(title);
        }

        if (request.Description is not null)
        {
            arguments.Add("-d");
            arguments.Add(request.Description);
        }

        if (request.Status is { Length: > 0 } status)
        {
            arguments.Add("-s");
            arguments.Add(status);
        }

        if (request.Type is { Length: > 0 } type)
        {
            arguments.Add("-t");
            arguments.Add(type);
        }

        if (request.Priority is { } priority)
        {
            arguments.Add("-p");
            arguments.Add(priority.ToString());
        }

        if (request.Assignee is { Length: > 0 } assignee)
        {
            arguments.Add("-a");
            arguments.Add(assignee);
        }

        if (request.Notes is { Length: > 0 } notes)
        {
            arguments.Add("--append-notes");
            arguments.Add(notes);
        }

        arguments.Add("--json");
        await RunWriteAsync(arguments, cancellationToken).ConfigureAwait(false);
    }

    public async Task CloseAsync(string id, string? reason, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var arguments = new List<string> { "close", id };
        if (!string.IsNullOrWhiteSpace(reason))
        {
            arguments.Add("-r");
            arguments.Add(reason);
        }

        arguments.Add("--json");
        await RunWriteAsync(arguments, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await RunWriteAsync(["delete", id, "--force"], cancellationToken).ConfigureAwait(false);
    }

    public async Task AddCommentAsync(string id, string text, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new BdException(BdErrorCode.InvalidArgument, "评论内容不能为空");
        }

        await RunWriteAsync(["comments", "add", id, text.Trim()], cancellationToken).ConfigureAwait(false);
    }

    public async Task AddLabelAsync(string id, string label, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        await RunWriteAsync(["label", "add", id, label.Trim()], cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveLabelAsync(string id, string label, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        await RunWriteAsync(["label", "remove", id, label.Trim()], cancellationToken).ConfigureAwait(false);
    }

    public async Task AddDependencyAsync(string id, string dependsOnId, DependencyType type, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(dependsOnId);
        await RunWriteAsync(
            ["dep", "add", id, dependsOnId, "--type", BdEnumMaps.ToBdString(type)],
            cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveDependencyAsync(string id, string dependsOnId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(dependsOnId);
        await RunWriteAsync(["dep", "remove", id, dependsOnId], cancellationToken).ConfigureAwait(false);
    }
}
