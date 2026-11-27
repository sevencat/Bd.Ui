using Avalonia.Headless.XUnit;
using Bd.App.Controls;
using Bd.Core.Services;
using FluentAssertions;
using Xunit;

namespace Bd.App.Tests;

public sealed class ChromeTests
{
    [AvaloniaFact]
    public void 提示浮层显示消息并可关闭()
    {
        var toast = new ToastService();
        var window = TestScene.Show(new ToastHost { DataContext = toast }, 600, 400);

        toast.Success("已保存");
        TestScene.Relayout(window);

        window.Texts().Should().Contain("已保存");
        toast.Items.Should().HaveCount(1);

        toast.Dismiss(toast.Items[0]);
        toast.Items.Should().BeEmpty();
        window.Close();
    }
}
