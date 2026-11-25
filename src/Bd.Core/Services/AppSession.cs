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
        Config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        ApplyToRuntime(Config);
    }

    public async Task ApplyAsync(AppConfig config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);
        var normalized = config.Normalize();
        await _configStore.SaveAsync(normalized, cancellationToken).ConfigureAwait(false);
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
