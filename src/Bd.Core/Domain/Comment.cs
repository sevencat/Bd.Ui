namespace Bd.Core.Domain;

public sealed record Comment
{
    public string Id { get; init; } = string.Empty;

    public string IssueId { get; init; } = string.Empty;

    public string Author { get; init; } = string.Empty;

    public string Text { get; init; } = string.Empty;

    public DateTimeOffset? CreatedAt { get; init; }
}
