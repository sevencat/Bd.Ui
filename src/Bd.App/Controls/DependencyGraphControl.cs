using System.Collections.Specialized;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Bd.Core.Domain;
using Bd.Core.Views;

namespace Bd.App.Controls;

/// <summary>轻量依赖图：自绘节点与折线边，避免引入第三方图表依赖。</summary>
public sealed class DependencyGraphControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<GraphNodeBox>?> NodesProperty =
        AvaloniaProperty.Register<DependencyGraphControl, IReadOnlyList<GraphNodeBox>?>(nameof(Nodes));

    public static readonly StyledProperty<IReadOnlyList<GraphEdge>?> EdgesProperty =
        AvaloniaProperty.Register<DependencyGraphControl, IReadOnlyList<GraphEdge>?>(nameof(Edges));

    public static readonly StyledProperty<string?> RootIdProperty =
        AvaloniaProperty.Register<DependencyGraphControl, string?>(nameof(RootId));

    private static readonly IBrush CanvasBrush = new SolidColorBrush(Color.Parse("#12151A"));
    private static readonly IBrush EdgeBrush = new SolidColorBrush(Color.Parse("#4A5568"));
    private static readonly IBrush NodeFill = new SolidColorBrush(Color.Parse("#1F2530"));
    private static readonly IBrush RootFill = new SolidColorBrush(Color.Parse("#1E3350"));
    private static readonly IBrush NodeBorder = new SolidColorBrush(Color.Parse("#2C3442"));
    private static readonly IBrush RootBorder = new SolidColorBrush(Color.Parse("#5B9DFF"));
    private static readonly IBrush TitleBrush = new SolidColorBrush(Color.Parse("#E8ECF3"));
    private static readonly IBrush SubtitleBrush = new SolidColorBrush(Color.Parse("#9AA5B5"));
    private static readonly IBrush ConnectBrush = new SolidColorBrush(Color.Parse("#5B9DFF"));

    private const double DragThreshold = 4;

    private GraphNodeBox? _dragSource;
    private Point _pressPoint;
    private Point _dragPoint;
    private bool _isDragging;

    static DependencyGraphControl()
    {
        AffectsRender<DependencyGraphControl>(NodesProperty, EdgesProperty, RootIdProperty);
    }

    public IReadOnlyList<GraphNodeBox>? Nodes
    {
        get => GetValue(NodesProperty);
        set => SetValue(NodesProperty, value);
    }

    public IReadOnlyList<GraphEdge>? Edges
    {
        get => GetValue(EdgesProperty);
        set => SetValue(EdgesProperty, value);
    }

    public string? RootId
    {
        get => GetValue(RootIdProperty);
        set => SetValue(RootIdProperty, value);
    }

    public event EventHandler<string>? NodeSelected;

    public event EventHandler<GraphConnectionRequest>? ConnectionRequested;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(CanvasBrush, new Rect(Bounds.Size));

        var nodes = Nodes ?? [];
        if (nodes.Count == 0)
        {
            var empty = new FormattedText(
                "没有可显示的节点",
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                13,
                SubtitleBrush);
            context.DrawText(empty, new Point(24, 24));
            return;
        }

        var lookup = nodes.ToDictionary(static node => node.Id, StringComparer.OrdinalIgnoreCase);
        var edgePen = new Pen(EdgeBrush, 1.4);

        foreach (var edge in Edges ?? [])
        {
            if (!lookup.TryGetValue(edge.From, out var from) || !lookup.TryGetValue(edge.To, out var to))
            {
                continue;
            }

            var start = new Point(from.X + from.Width, from.Y + (from.Height / 2));
            var end = new Point(to.X, to.Y + (to.Height / 2));
            var midX = (start.X + end.X) / 2;
            context.DrawLine(edgePen, start, new Point(midX, start.Y));
            context.DrawLine(edgePen, new Point(midX, start.Y), new Point(midX, end.Y));
            context.DrawLine(edgePen, new Point(midX, end.Y), end);
            context.DrawLine(edgePen, end, new Point(end.X - 7, end.Y - 4));
            context.DrawLine(edgePen, end, new Point(end.X - 7, end.Y + 4));
        }

        foreach (var box in nodes)
        {
            var rect = new Rect(box.X, box.Y, box.Width, box.Height);

            var fill = box.IsRoot ? RootFill : NodeFill;
            var pen = new Pen(box.IsRoot ? RootBorder : NodeBorder, box.IsRoot ? 1.8 : 1.0);
            context.DrawRectangle(fill, pen, rect, 8, 8, default);

            var title = new FormattedText(
                Truncate(box.Title, 18),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                12.5,
                TitleBrush);
            context.DrawText(title, new Point(rect.X + 10, rect.Y + 9));

            var subtitle = new FormattedText(
                box.Id,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                10.5,
                SubtitleBrush);
            context.DrawText(subtitle, new Point(rect.X + 10, rect.Y + 32));
        }

        if (_isDragging && _dragSource is { } source)
        {
            var start = new Point(source.X + source.Width, source.Y + (source.Height / 2));
            var pen = new Pen(ConnectBrush, 2, new DashStyle([4, 3], 0));
            context.DrawLine(pen, start, _dragPoint);
            context.DrawEllipse(ConnectBrush, null, _dragPoint, 4, 4);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != NodesProperty)
        {
            return;
        }

        if (change.OldValue is INotifyCollectionChanged oldCollection)
        {
            oldCollection.CollectionChanged -= OnNodesChanged;
        }

        if (change.NewValue is INotifyCollectionChanged newCollection)
        {
            newCollection.CollectionChanged += OnNodesChanged;
        }

        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var position = e.GetPosition(this);
        var box = HitTestNode(position);
        if (box is null)
        {
            return;
        }

        _dragSource = box;
        _pressPoint = position;
        _dragPoint = position;
        _isDragging = false;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragSource is null)
        {
            return;
        }

        var position = e.GetPosition(this);
        if (!_isDragging)
        {
            var dx = position.X - _pressPoint.X;
            var dy = position.Y - _pressPoint.Y;
            if ((dx * dx) + (dy * dy) < DragThreshold * DragThreshold)
            {
                return;
            }

            _isDragging = true;
        }

        _dragPoint = position;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        var source = _dragSource;
        var wasDragging = _isDragging;
        var position = e.GetPosition(this);
        EndDrag(e.Pointer);

        if (source is null)
        {
            return;
        }

        if (!wasDragging)
        {
            NodeSelected?.Invoke(this, source.Id);
            e.Handled = true;
            return;
        }

        var target = HitTestNode(position);
        if (target is null || string.Equals(target.Id, source.Id, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ConnectionRequested?.Invoke(this, new GraphConnectionRequest(source.Id, target.Id));
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_dragSource is not null)
        {
            EndDrag(null);
        }
    }

    private GraphNodeBox? HitTestNode(Point position)
    {
        if (Nodes is not { } nodes)
        {
            return null;
        }

        foreach (var box in nodes)
        {
            if (new Rect(box.X, box.Y, box.Width, box.Height).Contains(position))
            {
                return box;
            }
        }

        return null;
    }

    private void EndDrag(IPointer? pointer)
    {
        _dragSource = null;
        _isDragging = false;
        if (pointer?.Captured is { } captured && ReferenceEquals(captured, this))
        {
            pointer.Capture(null);
        }

        InvalidateVisual();
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : string.Concat(value.AsSpan(0, max - 1), "…");

    private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();
}
