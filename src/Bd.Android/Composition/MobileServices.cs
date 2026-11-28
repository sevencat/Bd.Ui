using Bd.Core.Abstractions;
using Bd.Core.Config;
using Bd.Core.Dolt;
using Bd.Core.Services;
using Bd.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Bd.Mobile.Composition;

/// <summary>
/// 手机端组合根：服务列表与 <c>Bd.App.Composition.ServiceRegistration</c> 保持一致，
/// 只额外注册手机端外壳（界面本身不复用桌面端视图）。
/// </summary>
public static class MobileServices
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();

        services.AddSingleton<StoreProvider>();
        services.AddSingleton<IBdStore>(static provider => provider.GetRequiredService<StoreProvider>());
        services.AddSingleton<IAppConfigStore>(static _ => new JsonAppConfigStore(ConfigFilePath()));
        services.AddSingleton(static _ => new DoltConnectionFactory());
        services.AddSingleton<ProjectCatalog>();
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddSingleton<ToastService>();
        services.AddSingleton(static _ => new AttributionService(AppConfig.Default));
        services.AddSingleton(static provider => new AppSession(
            provider.GetRequiredService<IAppConfigStore>(),
            CreateStore,
            provider.GetRequiredService<StoreProvider>(),
            provider.GetRequiredService<AttributionService>()));

        services.AddSingleton<BoardService>();
        services.AddSingleton<BeadDetailService>();
        services.AddSingleton<BeadWriteService>();
        services.AddSingleton<ArchiveService>();
        services.AddSingleton<CommentService>();
        services.AddSingleton<EpicProgressService>();

        services.AddSingleton<BoardViewModel>();
        services.AddSingleton<BeadDetailViewModel>();
        services.AddSingleton<MobileSettingsViewModel>();
        services.AddSingleton<MobileShellViewModel>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    /// <summary>
    /// 手机上配置写 App 私有目录（FilesDir），与桌面端的 AppData 互不影响；
    /// 拿不到目录时退回默认路径，只影响能否把设置落盘。
    /// </summary>
    private static string? ConfigFilePath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        return string.IsNullOrWhiteSpace(root) ? null : Path.Combine(root, "Bd.Ui", "config.json");
    }

    private static IBdStore CreateStore(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (DemoDataProvider.IsDemoRequested(config))
        {
            return DemoDataProvider.CreateStore();
        }

        var settings = DoltConnectionSettings.FromConfig(config);
        var factory = new DoltConnectionFactory();

        if (config.DoltAggregateAll)
        {
            return CompositeBdStore.ForServer(factory, settings, new ProjectCatalog(factory));
        }

        return ReadOnlyGuard.Wrap(new DoltStore(factory, settings), config.ReadOnly);
    }
}
