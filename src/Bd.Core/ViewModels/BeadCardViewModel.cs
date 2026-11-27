using Bd.Core.Domain;
using Bd.Core.Views;

namespace Bd.Core.ViewModels;

public sealed class BeadCardViewModel
{
    public BeadCardViewModel(Bead bead, Attribution attribution, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(bead);
        Model = bead;
        Id = bead.Id;
        Title = BeadViewHelpers.Truncate(bead.DisplayTitle, 64);
        TypeText = BdEnumMaps.FormatType(bead.Type);
        PriorityText = BdEnumMaps.FormatPriority(bead.Priority);
        StatusText = BdEnumMaps.FormatStatus(bead.Status);
        OwnerText = string.IsNullOrWhiteSpace(bead.Owner) ? "未指派" : bead.Owner;
        RelativeUpdated = BeadViewHelpers.RelativeTime(bead.UpdatedAt, now);
        CommentCount = bead.CommentCount;
        DependencyCount = bead.DependencyCount;
        IsBlocked = bead.IsOpenBlocker || bead.Status == BeadStatus.Blocked;
        AttributionText = attribution.Kind switch
        {
            AttributionKind.Human => "human",
            AttributionKind.Agent => "agent",
            _ => "unknown"
        };
        AttributionKey = attribution.Kind switch
        {
            AttributionKind.Human => "human",
            AttributionKind.Agent => "agent",
            _ => "unknown"
        };
        IsArchived = bead.IsArchived;
        IsEpic = bead.IsEpic;
        ProjectText = bead.Project ?? string.Empty;
    }

    public Bead Model { get; }

    public string Id { get; }

    public string Title { get; }

    public string TypeText { get; }

    public string PriorityText { get; }

    public string StatusText { get; }

    public string OwnerText { get; }

    public string RelativeUpdated { get; }

    public int CommentCount { get; }

    public int DependencyCount { get; }

    public bool IsBlocked { get; }

    public bool IsArchived { get; }

    public bool IsEpic { get; }

    public string AttributionText { get; }

    public string AttributionKey { get; }

    /// <summary>来源项目（聚合模式下有值，单项目模式为空字符串）。</summary>
    public string ProjectText { get; }
}
