using Bd.Core.Domain;

namespace Bd.Core.Filters;

public sealed record BoardFilter
{
    public string? SearchText { get; init; }

    public BeadType? Type { get; init; }

    public BeadPriority? Priority { get; init; }

    public IReadOnlyList<string> Labels { get; init; } = [];

    public string? Assignee { get; init; }

    public bool IncludeArchived { get; init; }

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(SearchText)
        && Type is null
        && Priority is null
        && Labels.Count == 0
        && string.IsNullOrWhiteSpace(Assignee);

    public bool Matches(Bead bead)
    {
        ArgumentNullException.ThrowIfNull(bead);

        if (bead.IsArchived && !IncludeArchived)
        {
            return false;
        }

        if (Type is { } type && bead.Type != type)
        {
            return false;
        }

        if (Priority is { } priority && bead.Priority != priority)
        {
            return false;
        }

        foreach (var label in Labels)
        {
            if (!bead.HasLabel(label))
            {
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(Assignee)
            && !string.Equals(bead.Owner?.Trim(), Assignee.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var keyword = SearchText.Trim();
            var hit = bead.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || bead.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || (bead.Description?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)
                || bead.Labels.Any(label => label.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            if (!hit)
            {
                return false;
            }
        }

        return true;
    }
}
