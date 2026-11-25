namespace Bd.Core.Domain;

public enum AttributionKind
{
    Unknown,
    Human,
    Agent
}

public sealed record Attribution
{
    public string? Actor { get; init; }

    public AttributionKind Kind { get; init; }

    public static Attribution Unknown { get; } = new() { Kind = AttributionKind.Unknown };

    public static Attribution Human(string? actor) => new() { Actor = actor, Kind = AttributionKind.Human };

    public static Attribution Agent(string? actor) => new() { Actor = actor, Kind = AttributionKind.Agent };
}
