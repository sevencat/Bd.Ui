using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Bd.Core.ViewModels;
using Bd.Mobile.Composition;
using Bd.Mobile.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Bd.Mobile;

/// <summary>Android 单视图应用的启动点：装配服务、挂上手机端外壳、开始拉数据。</summary>
/// <remarks>
/// 必须写全 <see cref="Avalonia.Application"/>：Android SDK 会隐式引入 <c>Android.App</c>，
/// 让 <c>Application</c> 变成二义性引用。
/// </remarks>
public partial class App : Avalonia.Application
{
    private ServiceProvider? _services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            _services = MobileServices.Build();
            var shell = _services.GetRequiredService<MobileShellViewModel>();
            singleView.MainView = new MobileShellView { DataContext = shell };

            // 首屏先渲染出来，再开始连库，避免启动时白屏。
            Dispatcher.UIThread.Post(() => _ = shell.InitializeAsync(), DispatcherPriority.Background);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
