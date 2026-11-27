using Bd.Core.Abstractions;
using Bd.Core.BdCli;
using Bd.Core.Config;
using Bd.Core.Dolt;
using Bd.Core.Services;
using Bd.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Bd.App.Composition;

/// <summary>唯一组合根：所有单例在此装配，View 只通过构造函数拿到 ViewModel。</summary>
public static class ServiceRegistration
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();

        services.AddSingleton<StoreProvider>();
        services.AddSingleton<IBdStore>(static sp => sp.GetRequiredService<StoreProvider>());
        services.AddSingleton<IAppConfigStore>(static _ => new JsonAppConfigStore());
        services.AddSingleton(static _ => new DoltConnectionFactory());
        services.AddSingleton<ProjectCatalog>();
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddSingleton<ToastService>();
        services.AddSingleton(static _ => new AttributionService(AppConfig.Default));
        services.AddSingleton(static sp => new AppSession(
            sp.GetRequiredService<IAppConfigStore>(),
            CreateStore,
            sp.GetRequiredService<StoreProvider>(),
            sp.GetRequiredService<AttributionService>()));

        services.AddSingleton<BoardService>();
        services.AddSingleton<BeadDetailService>();
        services.AddSingleton<BeadWriteService>();
        services.AddSingleton<ArchiveService>();
        services.AddSingleton<CommentService>();
        services.AddSingleton<EpicProgressService>();
        services.AddSingleton<DoctorService>();

        services.AddSingleton<BoardViewModel>();
        services.AddSingleton<EpicsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<BeadDetailViewModel>();
        services.AddSingleton<BeadEditorViewModel>();
        services.AddSingleton<ShellViewModel>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    private static IBdStore CreateStore(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        // 显式开启演示模式时不连库、不启动子进程。
        if (DemoDataProvider.IsDemoRequested(config))
        {
            return DemoDataProvider.CreateStore();
        }

        // 直连 Dolt 不依赖 bd 可执行文件，因此不能套用“bd 缺失即演示数据”的兜底。
        if (config.StorageBackend == StorageBackends.Dolt)
        {
            var settings = DoltConnectionSettings.FromConfig(config);
            var factory = new DoltConnectionFactory();

            // 「全部项目」：多库合并视图，自身已拒绝写入。
            if (config.DoltAggregateAll)
            {
                return CompositeBdStore.ForServer(factory, settings, new ProjectCatalog(factory));
            }

            // 写路径已 SQL 化，只读与否交给「设置 → 只读模式」。
            return ReadOnlyGuard.Wrap(new DoltStore(factory, settings), config.ReadOnly);
        }

        if (!DemoDataProvider.IsBdAvailable(config.BdBinaryPath))
        {
            return DemoDataProvider.CreateStore();
        }

        var repo = ResolveRepoPath(config.RepoPath);
        var runner = new BdProcessRunner(config.BdBinaryPath);
        var store = new BdCliStore(runner, repo, config.HumanActor);
        return ReadOnlyGuard.Wrap(store, config.ReadOnly);
    }

    private static string ResolveRepoPath(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".beads")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Environment.CurrentDirectory;
    }
}
