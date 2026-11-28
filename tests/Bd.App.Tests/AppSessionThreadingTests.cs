using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Bd.Core.Abstractions;
using Bd.Core.Config;
using Bd.Core.Services;
using FluentAssertions;
using Xunit;

namespace Bd.App.Tests;

/// <summary>
/// 切换项目/保存设置都会走 <see cref="AppSession.ApplyAsync"/>，
/// 它触发的 <see cref="AppSession.ConfigChanged"/> 会直接改动主题与项目下拉等 UI 绑定对象，
/// 因此必须回到 UI 线程，否则会抛 “The calling thread cannot access this object”。
/// </summary>
public sealed class AppSessionThreadingTests
{
    [AvaloniaFact]
    public async Task 配置变更事件回到UI线程触发()
    {
        var session = new AppSession(
            new AsyncConfigStore(AppConfig.Default),
            _ => DemoDataProvider.CreateStore(),
            new StoreProvider(),
            new AttributionService(AppConfig.Default));

        var onUiThread = false;
        session.ConfigChanged += (_, _) => onUiThread = Dispatcher.UIThread.CheckAccess();

        await session.ApplyAsync(session.Config with { Theme = "Light" }, CancellationToken.None);

        onUiThread.Should().BeTrue("配置变更的订阅者会改动 UI 绑定对象");
    }

    /// <summary>保存时真的异步让出，模拟写 config.json 的文件 IO，便于暴露线程切换。</summary>
    private sealed class AsyncConfigStore(AppConfig initial) : IAppConfigStore
    {
        public string FilePath => "memory://bd-ui/config.json";

        public Task<AppConfig> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(initial);

        public async Task SaveAsync(AppConfig config, CancellationToken cancellationToken)
        {
            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        }
    }
}
