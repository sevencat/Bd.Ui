using Bd.Core.Domain;

namespace Bd.Core.Views;

/// <summary>纯函数展示派生：列归属、排序、相对时间、截断，全部零 Avalonia 依赖。</summary>
public static class BeadViewHelpers
{
    public static BoardColumnKind ResolveColumn(Bead bead)
    {
        ArgumentNullException.ThrowIfNull(bead);

        if (bead.IsClosed)
        {
            return BoardColumnKind.Done;
        }

        return bead.Status switch
        {
            BeadStatus.Deferred => BoardColumnKind.Backlog,
            BeadStatus.Blocked => BoardColumnKind.Blocked,
            BeadStatus.InProgress => BoardColumnKind.InProgress,
            _ => bead.IsOpenBlocker ? BoardColumnKind.Blocked : BoardColumnKind.Ready
        };
    }

    public static IReadOnlyList<Bead> SortForColumn(IEnumerable<Bead> beads) =>
    [
        .. beads
            .OrderBy(static bead => bead.PriorityValue)
            .ThenByDescending(static bead => bead.UpdatedAt ?? DateTimeOffset.MinValue)
            .ThenBy(static bead => bead.Id, StringComparer.Ordinal)
    ];

    public static string RelativeTime(DateTimeOffset? timestamp, DateTimeOffset now)
    {
        if (timestamp is not { } value)
        {
            return "—";
        }

        var delta = now - value;
        if (delta < TimeSpan.Zero)
        {
            delta = TimeSpan.Zero;
        }

        if (delta.TotalMinutes < 1)
        {
            return "刚刚";
        }

        if (delta.TotalMinutes < 60)
        {
            return $"{(int)delta.TotalMinutes} 分钟前";
        }

        if (delta.TotalHours < 24)
        {
            return $"{(int)delta.TotalHours} 小时前";
        }

        if (delta.TotalDays < 30)
        {
            return $"{(int)delta.TotalDays} 天前";
        }

        return value.ToLocalTime().ToString("yyyy-MM-dd");
    }

    public static string Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Length <= maxLength ? text : text[..Math.Max(0, maxLength - 1)] + "…";
    }

    public static string ProgressText(int closed, int total) =>
        total <= 0 ? "0 / 0" : $"{closed} / {total}";

    public static double ProgressRatio(int closed, int total) =>
        total <= 0 ? 0 : Math.Clamp((double)closed / total, 0, 1);
}
