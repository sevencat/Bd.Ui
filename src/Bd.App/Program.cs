using Avalonia;
using SimdSkia.Avalonia;

namespace Bd.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseStandardRuntimePlatformSubsystem()
            .UseWin32()
            .With(new Win32PlatformOptions
            {
                RenderingMode = [Win32RenderingMode.Software]
            })
            .UseSimdSkia(static options => SimdSkiaGpuOptions.UseGpuPreferred(options))
            .LogToTrace();
}
