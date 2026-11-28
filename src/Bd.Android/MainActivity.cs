using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia;
using Avalonia.Android;
using Avalonia.Controls.ApplicationLifetimes;
using Bd.Core.ViewModels;

namespace Bd.Mobile;

/// <summary>
/// 唯一 Activity：Avalonia 单视图宿主。屏幕旋转 / 尺寸变化不重建 Activity，
/// 否则每次转屏都会重新连一次 Dolt。
/// </summary>
[Activity(
    Label = "Bd.Ui",
    Theme = "@style/BdTheme.NoActionBar",
    Icon = "@drawable/beads",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : AvaloniaMainActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // 详情页按返回先回列表；已在列表则不拦截，交给系统退出应用。
        BackRequested += OnBackRequested;
    }

    protected override void OnDestroy()
    {
        BackRequested -= OnBackRequested;
        base.OnDestroy();
    }

    private static void OnBackRequested(object? sender, AndroidBackRequestedEventArgs e)
    {
        if (Shell is { CanGoBack: true } shell)
        {
            shell.GoBack();
            e.Handled = true;
        }
    }

    /// <summary>手机端只有一屏，直接从单视图生命周期里取外壳 ViewModel。</summary>
    private static MobileShellViewModel? Shell =>
        // 必须带 global:: —— Activity 自己有实例属性 Application，会遮住类型名。
        global::Avalonia.Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime
        {
            MainView: { DataContext: MobileShellViewModel shell }
        }
            ? shell
            : null;
}
