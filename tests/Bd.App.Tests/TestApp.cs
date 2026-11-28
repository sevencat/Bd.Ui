using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Themes.Fluent2;
using Bd.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Bd.App.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}

/// <summary>不带 DI 与真实 bd 的最小应用，只加载主题与令牌，供 headless 渲染用。</summary>
public sealed class TestApplication : Application
{
    public override void Initialize()
    {
        Styles.Add(new Fluent2Theme());

        if (AvaloniaXamlLoader.Load(new Uri("avares://Bd.Ui/Styles/Tokens.axaml")) is ResourceDictionary tokens)
        {
            Resources.MergedDictionaries.Add(tokens);
        }

        if (AvaloniaXamlLoader.Load(new Uri("avares://Bd.Ui/Styles/Controls.axaml")) is Styles controls)
        {
            Styles.Add(controls);
        }

        Resources.Add("HexBrush", Bd.App.Converters.HexBrushConverter.Instance);
    }
}
