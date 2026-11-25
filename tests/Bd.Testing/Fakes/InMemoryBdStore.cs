using Bd.Core.Abstractions;
using Bd.Core.BdCli;
using Bd.Core.Domain;

namespace Bd.Testing.Fakes;

/// <summary>服务层单测用的内存存储，行为对齐 bd 的可见语义。</summary>
public sealed class InMemoryBdStore : IBdStore
{
    public List<Bead> Beads { get; } = [];

    public List<Comment> Comments { get; } = [];

    public List<string> Calls { get; } = [];

    public List<DependencyType> RecordedDependencyTypes { get; } = [];

    public string BackendName => "in-memory";

    public Task<IReadOnlyList<Bead>> ListAsync(BeadListQuery query, CancellationToken cancellationToken)
    {
        Calls.Add($"list:{query.Status ?? "*"}");
        IEnumerable<Bead> result = Beads;

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            result = result.Where(bead => string.Equals(bead.StatusRaw, query.Status, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.Label))
        {
            result = result.Where(bead => bead.HasLabel(query.Label));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var keyword = query.Search;
            result = result.Where(bead =>
                bead.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || bead.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        if (query.Limit > 0)
        {
            result = result.Take(query.Limit);
        }

        return Task.FromResult<IReadOnlyList<Bead>>([.. result]);
    }

    public Task<IReadOnlyList<Bead>> ReadyAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Bead>>(
            [.. Beads.Where(bead => bead.Status == BeadStatus.Open && !bead.IsOpenBlocker)]);

    public Task<IReadOnlyList<Bead>> BlockedAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Bead>>(
            [.. Beads.Where(bead => bead.Status == BeadStatus.Blocked || bead.IsOpenBlocker)]);

    public Task<Bead> GetAsync(string id, CancellationToken cancellationToken)
    {
        var bead = Beads.FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase));
        return bead is null
            ? throw BdException.NotFound(id)
            : Task.FromResult(bead);
    }

    public Task<IReadOnlyList<DependencyLink>> ListDependenciesAsync(string id, CancellationToken cancellationToken)
    {
        var bead = Beads.FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(bead?.Dependencies ?? (IReadOnlyList<DependencyLink>)[]);
    }

    public Task<IReadOnlyList<Comment>> ListCommentsAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Comment>>(
            [.. Comments.Where(comment => string.Equals(comment.IssueId, id, StringComparison.OrdinalIgnoreCase))]);

    public Task<string> GetVersionAsync(CancellationToken cancellationToken) => Task.FromResult("1.3.4");

    public Task<DoctorReport> RunDoctorAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new DoctorReport { Ok = true, BdVersion = "1.3.4", Summary = "ok" });

    public Task<string> CreateAsync(CreateBeadRequest request, CancellationToken cancellationToken)
    {
        Calls.Add("create");
        var id = $"new-{Beads.Count + 1}";
        Beads.Add(new Bead
        {
            Id = id,
            Title = request.Title,
            Description = request.Description,
            StatusRaw = "open",
            PriorityValue = request.Priority,
            TypeRaw = request.Type,
            Owner = request.Assignee,
            ParentId = request.ParentId,
            Labels = request.Labels
        });
        return Task.FromResult(id);
    }

    public Task UpdateAsync(string id, UpdateBeadRequest request, CancellationToken cancellationToken)
    {
        Calls.Add("update");
        var index = Beads.FindIndex(bead => string.Equals(bead.Id, id, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            throw BdException.NotFound(id);
        }

        var current = Beads[index];
        Beads[index] = current with
        {
            Title = request.Title ?? current.Title,
            Description = request.Description ?? current.Description,
            StatusRaw = request.Status ?? current.StatusRaw,
            PriorityValue = request.Priority ?? current.PriorityValue,
            Owner = request.Assignee ?? current.Owner
        };
        return Task.CompletedTask;
    }

    public Task CloseAsync(string id, string? reason, CancellationToken cancellationToken)
    {
        Calls.Add("close");
        return UpdateAsync(id, new UpdateBeadRequest { Status = "closed" }, cancellationToken);
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        Calls.Add("delete");
        Beads.RemoveAll(bead => string.Equals(bead.Id, id, StringComparison.OrdinalIgnoreCase));
        return Task.CompletedTask;
    }

    public Task AddCommentAsync(string id, string text, CancellationToken cancellationToken)
    {
        Calls.Add("comment");
        Comments.Add(new Comment
        {
            Id = $"c{Comments.Count + 1}",
            IssueId = id,
            Author = "tester",
            Text = text,
            CreatedAt = DateTimeOffset.UtcNow
        });
        return Task.CompletedTask;
    }

    public Task AddLabelAsync(string id, string label, CancellationToken cancellationToken)
    {
        Calls.Add($"label+{label}");
        return Mutate(id, bead => bead with { Labels = [.. bead.Labels, label] });
    }

    public Task RemoveLabelAsync(string id, string label, CancellationToken cancellationToken)
    {
        Calls.Add($"label-{label}");
        return Mutate(id, bead => bead with
        {
            Labels = [.. bead.Labels.Where(value => !string.Equals(value, label, StringComparison.OrdinalIgnoreCase))]
        });
    }

    public Task AddDependencyAsync(string id, string dependsOnId, DependencyType type, CancellationToken cancellationToken)
    {
        Calls.Add($"dep+{type}");
        RecordedDependencyTypes.Add(type);
        return Mutate(id, bead => bead with
        {
            Dependencies =
            [
                .. bead.Dependencies,
                new DependencyLink { TargetId = dependsOnId, DependencyTypeRaw = BdEnumMaps.ToBdString(type) }
            ]
        });
    }

    public Task RemoveDependencyAsync(string id, string dependsOnId, CancellationToken cancellationToken)
    {
        Calls.Add("dep-");
        return Mutate(id, bead => bead with
        {
            Dependencies = [.. bead.Dependencies.Where(link => !string.Equals(link.TargetId, dependsOnId, StringComparison.OrdinalIgnoreCase))]
        });
    }

    private Task Mutate(string id, Func<Bead, Bead> transform)
    {
        var index = Beads.FindIndex(bead => string.Equals(bead.Id, id, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            throw BdException.NotFound(id);
        }

        Beads[index] = transform(Beads[index]);
        return Task.CompletedTask;
    }
}
