using Bd.Core.Abstractions;
using Bd.Core.Config;

namespace Bd.Core.Services;

/// <summary>持有当前配置与存储实例，设置变更后原地替换，避免重建整个 DI 图。</summary>
public sealed class AppSession
{
    private readonly IAppConfigStore _configStore;
    private readonly Func<AppConfig, IBdStore> _storeFactory;
    private readonly StoreProvider _store;
    private readonly AttributionService _attribution;

    public AppSession(
        IAppConfigStore configStore,
        Func<AppConfig, IBdStore> storeFactory,
        StoreProvider store,
        AttributionService attribution)
    {
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        _storeFactory = storeFactory ?? throw new ArgumentNullException(nameof(storeFactory));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _attribution = attribution ?? throw new ArgumentNullException(nameof(attribution));
    }

    public AppConfig Config { get; private set; } = AppConfig.Default;

    public event EventHandler<AppConfig>? ConfigChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        // 不要 ConfigureAwait(false)：调用方（ViewModel）依赖回到原线程继续工作。
        Config = await _configStore.LoadAsync(cancellationToken);
        ApplyToRuntime(Config);
    }

    public async Task ApplyAsync(AppConfig config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);
        var normalized = config.Normalize();

        // ConfigChanged 会同步驱动主题、项目列表等 UI 绑定状态，
        // 必须留在调用方线程（通常是 UI 线程）上触发；切到线程池会抛跨线程访问异常。
        await _configStore.SaveAsync(normalized, cancellationToken);
        Config = normalized;
        ApplyToRuntime(normalized);
        ConfigChanged?.Invoke(this, normalized);
    }

    public void UpdateTheme(string theme)
    {
        Config = Config with { Theme = theme };
        ConfigChanged?.Invoke(this, Config);
    }

    private void ApplyToRuntime(AppConfig config)
    {
        _store.Configure(_storeFactory(config));
        _attribution.Update(config);
    }
}
