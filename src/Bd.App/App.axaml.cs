using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Bd.App.Composition;
using Bd.App.Views;
using Bd.Core.Config;
using Bd.Core.Logging;
using Bd.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bd.App;

public partial class App : Application
{
    private FileLoggerProvider? _logProvider;
    private ILogger? _logger;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        StartLogging();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var provider = ServiceRegistration.Build();
            var shell = provider.GetRequiredService<ShellViewModel>();
            var window = new MainWindow { DataContext = shell };

            desktop.MainWindow = window;
            window.Opened += (_, _) => _ = shell.InitializeAsync();
            desktop.ShutdownRequested += (_, _) => _ = shell.DisposeAsync();
            desktop.Exit += (_, _) => StopLogging();
            ScheduleSmokeExit(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>发布冒烟用：设置 BD_UI_SMOKE_EXIT_SECONDS 后自动正常退出。</summary>
    private static void ScheduleSmokeExit(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable("BD_UI_SMOKE_EXIT_SECONDS"), out var seconds)
            || seconds <= 0)
        {
            return;
        }

        DispatcherTimer.RunOnce(() => desktop.Shutdown(), TimeSpan.FromSeconds(seconds));
    }

    private void StartLogging()
    {
        FileLoggerOptions options;
        try
        {
            var config = new JsonAppConfigStore().LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            options = FileLoggerOptions.FromConfig(config);
            _logProvider = new FileLoggerProvider(options);
            _logger = _logProvider.CreateLogger("Bd.App");
        }
        catch (Exception ex)
        {
            // 日志初始化失败不影响启动，只是没有落盘日志。
            Trace.WriteLine($"文件日志初始化失败：{ex.Message}");
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;

        _logger?.LogInformation(
            "应用启动：日志目录 {Directory}，上限 {MaxKb} KB，保留 {Days} 天",
            _logProvider.LogDirectory,
            options.MaxFileBytes / 1024,
            options.RetentionDays);
    }

    private void StopLogging()
    {
        AppDomain.CurrentDomain.UnhandledException -= OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        Dispatcher.UIThread.UnhandledException -= OnDispatcherUnhandledException;

        _logger?.LogInformation("应用退出");
        _logger = null;
        _logProvider?.Dispose();
        _logProvider = null;
    }

    private void OnDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e) =>
        _logger?.LogCritical(e.ExceptionObject as Exception, "应用域未处理异常，进程即将终止");

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "未观察的任务异常");
        e.SetObserved();
    }

    private void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogCritical(e.Exception, "UI 线程未处理异常，已记录并继续运行");
        e.Handled = true;
    }
}
