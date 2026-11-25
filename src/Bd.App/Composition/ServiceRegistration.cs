using Bd.Core.Abstractions;
using Bd.Core.BdCli;
using Bd.Core.Config;
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
        services.AddSingleton<DependencyService>();
        services.AddSingleton<EpicProgressService>();
        services.AddSingleton<DoctorService>();

        services.AddSingleton<BoardViewModel>();
        services.AddSingleton<EpicsViewModel>();
        services.AddSingleton<GraphViewModel>();
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

        // bd 缺失或显式开启演示模式时不启动子进程，直接用内置数据保证界面可浏览。
        if (DemoDataProvider.ShouldUseDemoData(config))
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
