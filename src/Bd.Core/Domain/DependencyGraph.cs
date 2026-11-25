namespace Bd.Core.Domain;

public sealed record GraphEdge(string From, string To, DependencyType Type);

public sealed record GraphData
{
    public IReadOnlyList<Bead> Nodes { get; init; } = [];

    public IReadOnlyList<GraphEdge> Edges { get; init; } = [];
}

/// <summary>有向图：From 依赖 To。用于添加依赖前的成环预检与 1 跳邻域取数。</summary>
public sealed class DependencyGraph
{
    private readonly Dictionary<string, List<string>> _outgoing = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Bead> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<GraphEdge> _edges = [];

    public IReadOnlyList<GraphEdge> Edges => _edges;

    public IReadOnlyDictionary<string, Bead> Nodes => _nodes;

    public static DependencyGraph From(IEnumerable<Bead> beads)
    {
        var graph = new DependencyGraph();
        foreach (var bead in beads)
        {
            graph._nodes[bead.Id] = bead;
            foreach (var dependency in bead.Dependencies)
            {
                graph.AddEdge(bead.Id, dependency.TargetId, dependency.Type);
            }
        }

        return graph;
    }

    public void AddEdge(string from, string to, DependencyType type)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
        {
            return;
        }

        if (!_outgoing.TryGetValue(from, out var targets))
        {
            targets = [];
            _outgoing[from] = targets;
        }

        if (!targets.Contains(to, StringComparer.OrdinalIgnoreCase))
        {
            targets.Add(to);
        }

        if (!_edges.Any(edge =>
                string.Equals(edge.From, from, StringComparison.OrdinalIgnoreCase)
                && string.Equals(edge.To, to, StringComparison.OrdinalIgnoreCase)))
        {
            _edges.Add(new GraphEdge(from, to, type));
        }
    }

    /// <summary>加入 from→to 后是否会成环：只需判断 to 是否已能到达 from。</summary>
    public bool WouldCreateCycle(string from, string to)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>();
        stack.Push(to);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            if (string.Equals(current, from, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (_outgoing.TryGetValue(current, out var targets))
            {
                foreach (var target in targets)
                {
                    stack.Push(target);
                }
            }
        }

        return false;
    }

    public IReadOnlyList<string> Neighbors(string id, int depth = 1)
    {
        var result = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { id };
        var frontier = new List<string> { id };

        for (var level = 0; level < depth; level++)
        {
            var next = new List<string>();
            foreach (var current in frontier)
            {
                if (_outgoing.TryGetValue(current, out var targets))
                {
                    foreach (var target in targets)
                    {
                        if (visited.Add(target))
                        {
                            next.Add(target);
                            result.Add(target);
                        }
                    }
                }

                foreach (var edge in _edges)
                {
                    if (string.Equals(edge.To, current, StringComparison.OrdinalIgnoreCase) && visited.Add(edge.From))
                    {
                        next.Add(edge.From);
                        result.Add(edge.From);
                    }
                }
            }

            frontier = next;
        }

        return result;
    }

    public GraphData ToData(IEnumerable<string>? ids = null)
    {
        var selected = ids is null
            ? null
            : new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);

        var nodes = _nodes.Values
            .Where(bead => selected is null || selected.Contains(bead.Id))
            .ToList();

        var edges = _edges
            .Where(edge => selected is null
                || (selected.Contains(edge.From) && selected.Contains(edge.To)))
            .ToList();

        return new GraphData { Nodes = nodes, Edges = edges };
    }
}
