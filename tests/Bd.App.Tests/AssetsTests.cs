using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using FluentAssertions;
using Xunit;

namespace Bd.App.Tests;

public sealed class AssetsTests
{
    [AvaloniaFact]
    public void 官方图标资源已打包()
    {
        using var ico = AssetLoader.Open(new Uri("avares://Bd.Ui/Assets/beads.ico"));
        ico.Length.Should().BeGreaterThan(0);

        using var png = AssetLoader.Open(new Uri("avares://Bd.Ui/Assets/beads.png"));
        png.Length.Should().BeGreaterThan(0);
    }

    [AvaloniaFact]
    public void 窗口图标可从资源加载()
    {
        using var stream = AssetLoader.Open(new Uri("avares://Bd.Ui/Assets/beads.ico"));
        var icon = new WindowIcon(stream);

        var window = new Window { Icon = icon };
        window.Icon.Should().BeSameAs(icon);
        window.Close();
    }
}
