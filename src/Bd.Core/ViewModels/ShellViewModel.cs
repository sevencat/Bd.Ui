using Bd.Core.Abstractions;
using Bd.Core.Config;
using Bd.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bd.Core.ViewModels;

public sealed record NavItem(string Key, string Title, string Glyph, object View);

/// <summary>外壳：导航、状态栏、轮询、详情抽屉与全局提示的编排者。</summary>
public sealed partial class ShellViewModel : ObservableObject, IAsyncDisposable
{
    private readonly AppSession _session;
    private readonly StoreProvider _store;
    private readonly ToastService _toast;
    private readonly RefreshCoordinator _coordinator;
    private bool _disposed;
    private bool _isDemo;

    public ShellViewModel(
        BoardViewModel board,
        EpicsViewModel epics,
        GraphViewModel graph,
        SettingsViewModel settings,
        BeadDetailViewModel detail,
        BeadEditorViewModel editor,
        AppSession session,
        StoreProvider store,
        ToastService toast)
    {
        Board = board ?? throw new ArgumentNullException(nameof(board));
        Epics = epics ?? throw new ArgumentNullException(nameof(epics));
        Graph = graph ?? throw new ArgumentNullException(nameof(graph));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Detail = detail ?? throw new ArgumentNullException(nameof(detail));
        Editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _toast = toast ?? throw new ArgumentNullException(nameof(toast));

        NavItems =
        [
            new("board", "看板", "▦", Board),
            new("epics", "Epics", "◆", Epics),
            new("graph", "依赖图", "⋈", Graph),
            new("settings", "设置", "⚙", Settings)
        ];

        SelectedNav = NavItems[0];

        Board.CardActivated += (_, id) => _ = OpenDetailSafeAsync(id);
        Epics.EpicActivated += (_, id) => _ = OpenDetailSafeAsync(id);
        Graph.NodeActivated += (_, id) => _ = OpenDetailSafeAsync(id);
        Detail.Closed += (_, _) => IsDetailOpen = false;
        Detail.Changed += (_, _) => _ = RefreshAllSafeAsync();
        Editor.Created += (_, id) => _ = AfterCreateSafeAsync(id);
        Settings.Applied += (_, _) =>
        {
            ApplyConfig(_session.Config);
            _ = RefreshAllSafeAsync();
        };

        _session.ConfigChanged += (_, config) => ApplyConfig(config);

        _coordinator = new RefreshCoordinator(RefreshAllAsync, SystemClock.Instance);
        _coordinator.RefreshFailed += (_, exception) => _toast.Handle(exception);
        _coordinator.Refreshed += (_, _) => UpdateStatus();
    }

    public BoardViewModel Board { get; }

    public EpicsViewModel Epics { get; }

    public GraphViewModel Graph { get; }

    public SettingsViewModel Settings { get; }

    public BeadDetailViewModel Detail { get; }

    public BeadEditorViewModel Editor { get; }

    public IReadOnlyList<NavItem> NavItems { get; }

    public ToastService Toasts => _toast;

    public event EventHandler<string>? ThemeChanged;

    public event EventHandler? CreateRequested;

    [ObservableProperty]
    private NavItem _selectedNav;

    [ObservableProperty]
    private bool _isDetailOpen;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isReadOnly;

    [ObservableProperty]
    private bool _isInitialized;

    [ObservableProperty]
    private string _statusText = "准备就绪";

    [ObservableProperty]
    private string _repoText = "未配置仓库";

    [ObservableProperty]
    private string _versionText = "bd ?";

    public object CurrentView => SelectedNav.View;

    partial void OnSelectedNavChanged(NavItem value)
    {
        OnPropertyChanged(nameof(CurrentView));
        if (value.Key == "graph" && _store.IsConfigured)
        {
            _ = Graph.RefreshCoreAsync(CancellationToken.None, silent: true);
        }

        if (value.Key == "epics")
        {
            _ = Epics.RefreshCoreAsync(CancellationToken.None, silent: true);
        }

        if (value.Key == "settings")
        {
            Settings.ReloadFromSession();
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            IsBusy = true;
            StatusText = "正在连接 bd...";
            await _session.InitializeAsync(cancellationToken);
            ApplyConfig(_session.Config);
            await RefreshAllAsync(cancellationToken);
            VersionText = await ReadVersionAsync(cancellationToken);
            _coordinator.SetInterval(_session.Config.PollInterval);
            _coordinator.Start();
            IsInitialized = true;
            StatusText = "已连接，自动刷新已开启";
        }
        catch (Exception ex)
        {
            _toast.Handle(ex);
            StatusText = "初始化失败，请检查设置";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        try
        {
            await _coordinator.RefreshNowAsync(CancellationToken.None);
            _toast.Success("已刷新");
        }
        catch (Exception ex)
        {
            _toast.Handle(ex);
        }
    }

    [RelayCommand]
    public void Navigate(string? key)
    {
        var item = NavItems.FirstOrDefault(candidate => string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase));
        if (item is not null)
        {
            SelectedNav = item;
        }
    }

    [RelayCommand]
    public async Task ToggleThemeAsync()
    {
        try
        {
            var next = string.Equals(_session.Config.Theme, "Dark", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark";
            await _session.ApplyAsync(_session.Config with { Theme = next }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _toast.Handle(ex);
        }
    }

    [RelayCommand]
    public void CloseDetail() => Detail.Close();

    [RelayCommand]
    public void CreateTask() => CreateRequested?.Invoke(this, EventArgs.Empty);

    public async Task OpenDetailAsync(string id)
    {
        await Detail.LoadAsync(id);
        IsDetailOpen = Detail.IsOpen;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _coordinator.DisposeAsync();
    }

    private async Task OpenDetailSafeAsync(string id)
    {
        try
        {
            await OpenDetailAsync(id);
        }
        catch (Exception ex)
        {
            _toast.Handle(ex);
        }
    }

    private async Task AfterCreateSafeAsync(string id)
    {
        await RefreshAllSafeAsync();
        await OpenDetailSafeAsync(id);
    }

    private async Task RefreshAllSafeAsync()
    {
        try
        {
            await RefreshAllAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _toast.Handle(ex);
        }
    }

    private async Task RefreshAllAsync(CancellationToken cancellationToken)
    {
        if (!_store.IsConfigured)
        {
            return;
        }

        await Board.RefreshCoreAsync(cancellationToken, silent: true);
        await Epics.RefreshCoreAsync(cancellationToken, silent: true);
        if (SelectedNav.Key is "graph")
        {
            await Graph.RefreshCoreAsync(cancellationToken, silent: true);
        }

        UpdateStatus();
    }

    private async Task<string> ReadVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            return _isDemo
                ? "演示数据"
                : "bd " + await _store.GetVersionAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            return _toast.Error(ex);
        }
    }

    private void ApplyConfig(AppConfig config)
    {
        _isDemo = _store.IsConfigured
            && string.Equals(_store.BackendName, DemoDataProvider.BackendName, StringComparison.Ordinal);
        IsReadOnly = config.ReadOnly || _isDemo;
        RepoText = _isDemo
            ? "演示数据 · 未连接 bd"
            : string.IsNullOrWhiteSpace(config.RepoPath) ? $"当前目录 · {Environment.CurrentDirectory}" : config.RepoPath;
        _coordinator.SetInterval(config.PollInterval);
        ThemeChanged?.Invoke(this, config.Theme);
        Settings.ReloadFromSession();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var at = _coordinator.LastRefreshedAt?.ToLocalTime().ToString("HH:mm:ss") ?? "—";
        var suffix = _isDemo ? " · 演示" : IsReadOnly ? " · 只读" : string.Empty;
        StatusText = $"刷新 {at} · {Board.TotalCount} 项 · 每 {_coordinator.Interval.TotalSeconds:0}s{suffix}";
    }
}
