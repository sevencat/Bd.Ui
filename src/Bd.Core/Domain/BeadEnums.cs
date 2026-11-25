namespace Bd.Core.Domain;

public enum BeadStatus
{
    Open,
    InProgress,
    Blocked,
    Deferred,
    Closed,
    Pinned,
    Unknown
}

public enum BeadType
{
    Task,
    Bug,
    Feature,
    Epic,
    Chore,
    Decision,
    Spike,
    Story,
    Milestone,
    Unknown
}

public enum DependencyType
{
    Blocks,
    ParentChild,
    Related,
    DiscoveredFrom,
    Duplicates,
    Supersedes,
    RepliesTo,
    Awaits,
    Unknown
}

public enum BeadPriority
{
    None = -1,
    P0 = 0,
    P1 = 1,
    P2 = 2,
    P3 = 3,
    P4 = 4
}

public static class BdLabels
{
    public const string Archived = "archived";
}

public static class BdEnumMaps
{
    private static readonly Dictionary<string, BeadStatus> StatusMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["open"] = BeadStatus.Open,
        ["in_progress"] = BeadStatus.InProgress,
        ["in-progress"] = BeadStatus.InProgress,
        ["blocked"] = BeadStatus.Blocked,
        ["deferred"] = BeadStatus.Deferred,
        ["closed"] = BeadStatus.Closed,
        ["done"] = BeadStatus.Closed,
        ["pinned"] = BeadStatus.Pinned
    };

    private static readonly Dictionary<string, BeadType> TypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["task"] = BeadType.Task,
        ["bug"] = BeadType.Bug,
        ["feature"] = BeadType.Feature,
        ["epic"] = BeadType.Epic,
        ["chore"] = BeadType.Chore,
        ["decision"] = BeadType.Decision,
        ["spike"] = BeadType.Spike,
        ["story"] = BeadType.Story,
        ["milestone"] = BeadType.Milestone
    };

    private static readonly Dictionary<string, DependencyType> DependencyMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["blocks"] = DependencyType.Blocks,
        ["parent-child"] = DependencyType.ParentChild,
        ["parent_child"] = DependencyType.ParentChild,
        ["related"] = DependencyType.Related,
        ["relates_to"] = DependencyType.Related,
        ["discovered-from"] = DependencyType.DiscoveredFrom,
        ["duplicates"] = DependencyType.Duplicates,
        ["supersedes"] = DependencyType.Supersedes,
        ["replies-to"] = DependencyType.RepliesTo,
        ["awaits"] = DependencyType.Awaits
    };

    public static BeadStatus ParseStatus(string? raw) =>
        raw is not null && StatusMap.TryGetValue(raw, out var value) ? value : BeadStatus.Unknown;

    public static string ToBdString(BeadStatus status) => status switch
    {
        BeadStatus.Open => "open",
        BeadStatus.InProgress => "in_progress",
        BeadStatus.Blocked => "blocked",
        BeadStatus.Deferred => "deferred",
        BeadStatus.Closed => "closed",
        BeadStatus.Pinned => "pinned",
        _ => "open"
    };

    public static BeadType ParseType(string? raw) =>
        raw is not null && TypeMap.TryGetValue(raw, out var value) ? value : BeadType.Unknown;

    public static string ToBdString(BeadType type) => type switch
    {
        BeadType.Task => "task",
        BeadType.Bug => "bug",
        BeadType.Feature => "feature",
        BeadType.Epic => "epic",
        BeadType.Chore => "chore",
        BeadType.Decision => "decision",
        BeadType.Spike => "spike",
        BeadType.Story => "story",
        BeadType.Milestone => "milestone",
        _ => "task"
    };

    public static DependencyType ParseDependency(string? raw) =>
        raw is not null && DependencyMap.TryGetValue(raw, out var value) ? value : DependencyType.Unknown;

    public static string ToBdString(DependencyType type) => type switch
    {
        DependencyType.Blocks => "blocks",
        DependencyType.ParentChild => "parent-child",
        DependencyType.Related => "related",
        DependencyType.DiscoveredFrom => "discovered-from",
        DependencyType.Duplicates => "duplicates",
        DependencyType.Supersedes => "supersedes",
        DependencyType.RepliesTo => "replies-to",
        DependencyType.Awaits => "awaits",
        _ => "related"
    };

    public static BeadPriority ToPriority(int? value) => value switch
    {
        null => BeadPriority.None,
        <= 0 => BeadPriority.P0,
        1 => BeadPriority.P1,
        2 => BeadPriority.P2,
        3 => BeadPriority.P3,
        _ => BeadPriority.P4
    };

    public static int ToBdPriority(BeadPriority priority) =>
        priority == BeadPriority.None ? 2 : (int)priority;

    public static BeadPriority ParsePriority(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return BeadPriority.None;
        }

        var text = raw.Trim();
        if (text.StartsWith('P') || text.StartsWith('p'))
        {
            text = text[1..];
        }

        return int.TryParse(text, out var value) ? ToPriority(value) : BeadPriority.None;
    }

    public static string FormatPriority(BeadPriority priority) =>
        priority == BeadPriority.None ? "P2" : $"P{(int)priority}";

    public static string FormatStatus(BeadStatus status) => status switch
    {
        BeadStatus.Open => "Open",
        BeadStatus.InProgress => "In Progress",
        BeadStatus.Blocked => "Blocked",
        BeadStatus.Deferred => "Backlog",
        BeadStatus.Closed => "Done",
        BeadStatus.Pinned => "Pinned",
        _ => "Unknown"
    };

    public static string FormatType(BeadType type) => type switch
    {
        BeadType.Task => "Task",
        BeadType.Bug => "Bug",
        BeadType.Feature => "Feature",
        BeadType.Epic => "Epic",
        BeadType.Chore => "Chore",
        BeadType.Decision => "Decision",
        BeadType.Spike => "Spike",
        BeadType.Story => "Story",
        BeadType.Milestone => "Milestone",
        _ => "Task"
    };
}
