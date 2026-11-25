using Bd.Core.Domain;

namespace Bd.Core.Views;

public sealed record GraphNodeBox(
    string Id,
    string Title,
    string StatusText,
    bool IsRoot,
    double X,
    double Y,
    double Width = 150,
    double Height = 54);

/// <summary>拖拽连线请求：从源节点拖到目标节点后，由视图弹出依赖类型选择。</summary>
public sealed record GraphConnectionRequest(string SourceId, string TargetId);

/// <summary>按「到汇点的最长路径」分层排布，纯函数，便于单测与自绘控件复用。</summary>
public static class GraphLayout
{
    public const double NodeWidth = 150;

    public const double NodeHeight = 54;

    public const double HorizontalGap = 46;

    public const double VerticalGap = 30;

    public static (IReadOnlyList<GraphNodeBox> Nodes, double Width, double Height) Layout(GraphData graph, string? rootId)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var outgoing = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in graph.Edges)
        {
            if (!outgoing.TryGetValue(edge.From, out var targets))
            {
                targets = [];
                outgoing[edge.From] = targets;
            }

            targets.Add(edge.To);
        }

        var depthCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int Depth(string id, HashSet<string> visiting)
        {
            if (depthCache.TryGetValue(id, out var cached))
            {
                return cached;
            }

            if (!visiting.Add(id) || !outgoing.TryGetValue(id, out var targets) || targets.Count == 0)
            {
                depthCache[id] = 0;
                return 0;
            }

            var depth = 0;
            foreach (var target in targets)
            {
                depth = Math.Max(depth, 1 + Depth(target, visiting));
            }

            visiting.Remove(id);
            depthCache[id] = depth;
            return depth;
        }

        var columns = new Dictionary<int, List<Bead>>();
        foreach (var node in graph.Nodes)
        {
            var depth = Depth(node.Id, []);
            if (!columns.TryGetValue(depth, out var list))
            {
                list = [];
                columns[depth] = list;
            }

            list.Add(node);
        }

        var ordered = new List<GraphNodeBox>();
        var maxRows = 1;
        foreach (var (depth, nodes) in columns.OrderBy(static pair => pair.Key))
        {
            nodes.Sort(static (left, right) => string.CompareOrdinal(left.Id, right.Id));
            maxRows = Math.Max(maxRows, nodes.Count);
            for (var index = 0; index < nodes.Count; index++)
            {
                var node = nodes[index];
                ordered.Add(new GraphNodeBox(
                    node.Id,
                    node.DisplayTitle,
                    BdEnumMaps.FormatStatus(node.Status),
                    string.Equals(node.Id, rootId, StringComparison.OrdinalIgnoreCase),
                    20 + (depth * (NodeWidth + HorizontalGap)),
                    20 + (index * (NodeHeight + VerticalGap))));
            }
        }

        var depthCount = columns.Count == 0 ? 1 : columns.Keys.Max() + 1;
        var width = 40 + (depthCount * NodeWidth) + ((depthCount - 1) * HorizontalGap);
        var height = 40 + maxRows + (maxRows * VerticalGap);
        return (ordered, Math.Max(width, 240), Math.Max(height, 160));
    }
}
