using Bd.Core.Abstractions;
using Bd.Core.Config;
using Bd.Core.Dolt;
using Bd.Core.Services;
using Bd.Core.ViewModels;
using Bd.Testing.Fakes;
using FluentAssertions;
using Xunit;

namespace Bd.Core.Tests;

/// <summary>手机端设置页：读当前配置、落盘、端口校验。</summary>
public sealed class MobileSettingsViewModelTests
{
    [Fact]
    public void 构造时载入当前生效配置()
    {
        var config = AppConfig.Default with { DoltHost = "10.0.0.5", DoltPort = 4406, DoltDatabase = "beads" };
        var (vm, _) = Create(config);

        vm.DoltHost.Should().Be("10.0.0.5");
        vm.DoltPortText.Should().Be("4406");
        vm.DoltDatabase.Should().Be("beads");
        vm.ConfigPath.Should().Be("memory://bd-ui/config.json");
    }

    [Fact]
    public async Task 保存后写回配置并触发重连事件()
    {
        var (vm, store) = Create(AppConfig.Default);
        var savedCount = 0;
        vm.Saved += (_, _) => savedCount++;

        vm.DoltHost = "192.168.1.8";
        vm.DoltPortText = "4406";
        vm.DoltDatabase = "beads_dev";
        await vm.SaveAsync();

        var saved = store.Saved.Should().ContainSingle().Subject;
        saved.DoltHost.Should().Be("192.168.1.8");
        saved.DoltPort.Should().Be(4406);
        saved.DoltDatabase.Should().Be("beads_dev");
        savedCount.Should().Be(1);
        vm.StatusMessage.Should().Contain("已保存");
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task 端口非法时不写盘()
    {
        var (vm, store) = Create(AppConfig.Default);
        var savedCount = 0;
        vm.Saved += (_, _) => savedCount++;

        vm.DoltPortText = "70000";
        await vm.SaveAsync();

        store.Saved.Should().BeEmpty();
        savedCount.Should().Be(0);
        vm.StatusMessage.Should().Contain("端口");
    }

    [Fact]
    public async Task 空主机回落默认值()
    {
        var (vm, store) = Create(AppConfig.Default);

        vm.DoltHost = "   ";
        vm.DoltUser = string.Empty;
        await vm.SaveAsync();

        var saved = store.Saved.Should().ContainSingle().Subject;
        saved.DoltHost.Should().Be(DoltDefaults.Host);
        saved.DoltUser.Should().Be(DoltDefaults.User);
    }

    private static (MobileSettingsViewModel Vm, RecordingConfigStore Store) Create(AppConfig config)
    {
        var store = new RecordingConfigStore(config);
        var provider = new StoreProvider();
        provider.Configure(new InMemoryBdStore());
        var attribution = new AttributionService(config);
        var session = new AppSession(store, _ => new InMemoryBdStore(), provider, attribution);
        // 真实 App 启动时先 Initialize 才有 session.Config；设置页读的就是它。
        session.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();

        return (new MobileSettingsViewModel(session, store, new DoltConnectionFactory(), new ToastService()), store);
    }

    /// <summary>记录每次写盘的配置，用来断言「保存了什么」。</summary>
    private sealed class RecordingConfigStore(AppConfig initial) : IAppConfigStore
    {
        public List<AppConfig> Saved { get; } = [];

        public string FilePath => "memory://bd-ui/config.json";

        public Task<AppConfig> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(initial);

        public Task SaveAsync(AppConfig config, CancellationToken cancellationToken)
        {
            Saved.Add(config);
            return Task.CompletedTask;
        }
    }
}
