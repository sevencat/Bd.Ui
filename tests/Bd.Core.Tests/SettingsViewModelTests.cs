using Bd.Core.Abstractions;
using Bd.Core.Config;
using Bd.Core.Dolt;
using Bd.Core.Services;
using Bd.Core.ViewModels;
using FluentAssertions;
using Xunit;

namespace Bd.Core.Tests;

public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task 保存时把_Dolt_开关写回配置()
    {
        var initial = new AppConfig { DoltDatabase = "king_bdui" };
        var configStore = new MemoryConfigStore(initial);
        var session = CreateSession(configStore, initial);
        await session.InitializeAsync(CancellationToken.None);

        var vm = new SettingsViewModel(
            session,
            new DoctorService(DemoDataProvider.CreateStore()),
            new ToastService(),
            new DoltConnectionFactory())
        {
            DoltHost = " 10.0.0.7 ",
            DoltDatabase = " king_bdui ",
            DoltUseTls = true,
            DoltAggregateAll = true
        };

        await vm.SaveCommand.ExecuteAsync(null);

        configStore.Saved.Should().NotBeNull();
        configStore.Saved!.DoltHost.Should().Be("10.0.0.7");
        configStore.Saved.DoltDatabase.Should().Be("king_bdui");
        configStore.Saved.DoltUseTls.Should().BeTrue();
        configStore.Saved.DoltAggregateAll.Should().BeTrue();
        configStore.Saved.DemoMode.Should().BeFalse();
    }

    [Fact]
    public async Task 密码写回配置并在重载后回显()
    {
        var initial = new AppConfig { DoltDatabase = "king_bdui" };
        var configStore = new MemoryConfigStore(initial);
        var session = CreateSession(configStore, initial);
        await session.InitializeAsync(CancellationToken.None);

        var vm = new SettingsViewModel(
            session,
            new DoctorService(DemoDataProvider.CreateStore()),
            new ToastService(),
            new DoltConnectionFactory())
        {
            DoltPassword = " p@ss word "
        };

        await vm.SaveCommand.ExecuteAsync(null);

        configStore.Saved!.DoltPassword.Should().Be("p@ss word");
        vm.ReloadFromSession();
        vm.DoltPassword.Should().Be("p@ss word");
    }

    [Fact]
    public async Task 密码留空表示改用环境变量()
    {
        var initial = new AppConfig { DoltDatabase = "king_bdui" };
        var configStore = new MemoryConfigStore(initial);
        var session = CreateSession(configStore, initial);
        await session.InitializeAsync(CancellationToken.None);

        var vm = new SettingsViewModel(
            session,
            new DoctorService(DemoDataProvider.CreateStore()),
            new ToastService(),
            new DoltConnectionFactory())
        {
            DoltPassword = "   "
        };

        await vm.SaveCommand.ExecuteAsync(null);

        configStore.Saved!.DoltPassword.Should().BeEmpty();
    }

    [Fact]
    public async Task 演示模式开关写回配置()
    {
        var initial = AppConfig.Default;
        var configStore = new MemoryConfigStore(initial);
        var session = CreateSession(configStore, initial);
        await session.InitializeAsync(CancellationToken.None);

        var vm = new SettingsViewModel(
            session,
            new DoctorService(DemoDataProvider.CreateStore()),
            new ToastService(),
            new DoltConnectionFactory())
        {
            DemoMode = true
        };

        await vm.SaveCommand.ExecuteAsync(null);

        configStore.Saved!.DemoMode.Should().BeTrue();
    }

    private static AppSession CreateSession(MemoryConfigStore configStore, AppConfig initial) =>
        new(
            configStore,
            _ => DemoDataProvider.CreateStore(),
            new StoreProvider(),
            new AttributionService(initial));

    private sealed class MemoryConfigStore(AppConfig initial) : IAppConfigStore
    {
        public AppConfig? Saved { get; private set; }

        public string FilePath => "memory://bd-ui/config.json";

        public Task<AppConfig> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(initial);

        public Task SaveAsync(AppConfig config, CancellationToken cancellationToken)
        {
            Saved = config;
            return Task.CompletedTask;
        }
    }
}
