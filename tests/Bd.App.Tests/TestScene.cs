using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Bd.Core.Domain;

namespace Bd.App.Tests;

internal static class TestScene
{
    public static Bead Bead(string id, string title, string status = "open", string type = "task", int priority = 2) => new()
    {
        Id = id,
        Title = title,
        StatusRaw = status,
        TypeRaw = type,
        PriorityValue = priority,
        Owner = "alice",
        CreatedBy = "alice",
        CreatedAt = DateTimeOffset.UtcNow.AddHours(-2),
        UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
    };

    public static Window Show(Control content, double width = 1280, double height = 800)
    {
        var window = new Window
        {
            Content = content,
            Width = width,
            Height = height
        };

        window.Show();
        Relayout(window);
        return window;
    }

    public static void Relayout(Window window)
    {
        var size = window.ClientSize;
        window.Measure(size);
        window.Arrange(new Rect(size));
        Dispatcher.UIThread.RunJobs();
    }

    public static IReadOnlyList<string> Texts(this Visual visual) =>
        [.. visual.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(static block => block.Text)
            .Where(static text => !string.IsNullOrEmpty(text))
            .Select(static text => text!)];
}
