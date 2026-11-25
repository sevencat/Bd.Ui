namespace Bd.Core.Domain;

public enum BoardColumnKind
{
    Backlog,
    Ready,
    InProgress,
    Blocked,
    Done
}

public sealed record BoardSnapshot
{
    public IReadOnlyList<Bead> Backlog { get; init; } = [];

    public IReadOnlyList<Bead> Ready { get; init; } = [];

    public IReadOnlyList<Bead> InProgress { get; init; } = [];

    public IReadOnlyList<Bead> Blocked { get; init; } = [];

    public IReadOnlyList<Bead> Done { get; init; } = [];

    public DateTimeOffset LoadedAt { get; init; }

    public int Total => Backlog.Count + Ready.Count + InProgress.Count + Blocked.Count + Done.Count;

    public IReadOnlyList<Bead> Column(BoardColumnKind kind) => kind switch
    {
        BoardColumnKind.Backlog => Backlog,
        BoardColumnKind.Ready => Ready,
        BoardColumnKind.InProgress => InProgress,
        BoardColumnKind.Blocked => Blocked,
        _ => Done
    };

    public static BoardSnapshot Empty(DateTimeOffset loadedAt) => new() { LoadedAt = loadedAt };
}
