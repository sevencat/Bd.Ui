using Bd.Core.Abstractions;
using Bd.Core.Config;
using Bd.Core.Dolt;
using Bd.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bd.Core.ViewModels;

/// <summary>外壳：左树右详情、工具条、状态栏、刷新与全局提示的编排者。</summary>
public sealed partial class ShellViewModel : ObservableObject, IAsyncDisposable
{
    private readonly AppSession _session;
    private readonly StoreProvider _store;
    private readonly ToastService _toast;
    private readonly ProjectCatalog _catalog;
    private readonly RefreshCoordinator _coordinator;
    private bool _disposed;
    private bool _isDemo;
    private bool _suppressProjectSwitch;

    public ShellViewModel(
        BoardViewModel board,
        EpicsViewModel epics,
        SettingsViewModel settings,
        BeadDetailViewModel detail,
        BeadEditorViewModel editor,
        AppSession session,
        StoreProvider store,
        ToastService toast,
        ProjectCatalog catalog)
    {
        Board = board ?? throw new ArgumentNullException(nameof(board));
        Epics = epics ?? throw new ArgumentNullException(nameof(epics));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Detail = detail ?? throw new ArgumentNullException(nameof(detail));
        Editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _toast = toast ?? throw new ArgumentNullException(nameof(toast));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

        Board.CardActivated += (_, id) => _ = OpenDetailSafeAsync(id);
        // 树面板上的「刷新」按钮由 Board 发出请求，真正刷新仍走外壳的统一入口。
        Board.RefreshRequested += (_, _) => RefreshCommand.Execute(null);
        Epics.EpicActivated += (_, id) => _ = OpenDetailSafeAsync(id);
        Detail.Closed += (_, _) =>
        {
            IsDetailOpen = false;
            Board.ClearSelection();
        };
        Detail.Changed += (_, _) => _ = RefreshAllSafeAsync();
        Detail.NavigateRequested += (_, id) => _ = NavigateToAsync(id);
        Editor.Created += (_, id) => _ = AfterCreateSafeAsync(id);
        Settings.Applied += (_, _) =>
        {
            ApplyConfig(_session.Config);
            _ = RefreshAllSafeAsync();
        };

        _session.ConfigChanged += (_, config) => ApplyConfig(config);

        _coordinator = new RefreshCoordinator(RefreshAllAsync, SystemClock.Instance);
        _coordinator.Refreshed += (_, _) => UpdateStatus();
    }

    public BoardViewModel Board { get; }

    public EpicsViewModel Epics { get; }

    public SettingsViewModel Settings { get; }

    public BeadDetailViewModel Detail { get; }

    public BeadEditorViewModel Editor { get; }

    public ToastService Toasts => _toast;

    public event EventHandler<string>? ThemeChanged;

    public event EventHandler? CreateRequested;

    /// <summary>请求打开设置对话框（由窗口层弹窗，ViewModel 不引用 UI）。</summary>
    public event EventHandler? SettingsRequested;

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
    private string _repoText = "未连接 Dolt";

    [ObservableProperty]
    private string _versionText = "dolt ?";

    /// <summary>服务器上可切换的项目（直连 Dolt 时才有内容）。</summary>
    [ObservableProperty]
    private IReadOnlyList<ProjectInfo> _projects = [];

    [ObservableProperty]
    private ProjectInfo? _currentProject;

    [ObservableProperty]
    private bool _isProjectSwitcherVisible;

    partial void OnCurrentProjectChanged(ProjectInfo? value)
    {
        if (_suppressProjectSwitch || value is null)
        {
            return;
        }

        if (IsCurrentProject(value))
        {
            return;
        }

        _ = SwitchProjectSafeAsync(value);
    }

    private bool IsCurrentProject(ProjectInfo project) =>
        project.IsAggregate
            ? _session.Config.DoltAggregateAll
            : !_session.Config.DoltAggregateAll
              && string.Equals(project.Database, _session.Config.DoltDatabase, StringComparison.OrdinalIgnoreCase);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            IsBusy = true;
            StatusText = "正在连接 Dolt...";
            await _session.InitializeAsync(cancellationToken);
            ApplyConfig(_session.Config);
            await RefreshAllAsync(cancellationToken);
            VersionText = await ReadVersionAsync(cancellationToken);
            await ReloadProjectsSafeAsync();
            IsInitialized = true;
            StatusText = "已连接";
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

    /// <summary>右上角「设置」按钮：重载表单并请窗口弹出对话框。</summary>
    [RelayCommand]
    public void OpenSettings()
    {
        Settings.ReloadFromSession();
        SettingsRequested?.Invoke(this, EventArgs.Empty);
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
        // 先清空旧内容并显示加载态，数据回来后再渲染，避免旧任务内容残留。
        Detail.BeginLoading(id);
        IsDetailOpen = true;

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

    /// <summary>父/子任务跳转：树里同步高亮并刷新右侧详情。</summary>
    private async Task NavigateToAsync(string id)
    {
        Board.RestoreSelection(id);
        await OpenDetailSafeAsync(id);
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
        Board.RestoreSelection(id);
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
        UpdateStatus();
    }

    private async Task<string> ReadVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_isDemo)
            {
                return "演示数据";
            }

            var version = await _store.GetVersionAsync(cancellationToken);
            return $"dolt {version}";
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
        IsReadOnly = config.ReadOnly || _isDemo || config.DoltAggregateAll;
        RepoText = _isDemo
            ? "演示数据 · 未连接 Dolt"
            : $"dolt://{config.DoltHost}:{config.DoltPort}/{(config.DoltAggregateAll ? ProjectInfo.AggregateDatabase + "（全部项目）" : config.DoltDatabase)}";
        ThemeChanged?.Invoke(this, config.Theme);
        Settings.ReloadFromSession();

        if (IsDoltBackend)
        {
            _ = ReloadProjectsSafeAsync();
        }
        else
        {
            SetProjects([], null);
        }

        UpdateStatus();
    }

    /// <summary>除显式演示模式外，唯一数据源就是 Dolt 服务器。</summary>
    private bool IsDoltBackend => !_isDemo;

    /// <summary>重新枚举服务器上的项目；仅直连 Dolt 时有效。</summary>
    [RelayCommand]
    public async Task ReloadProjectsAsync()
    {
        if (!IsDoltBackend)
        {
            SetProjects([], null);
            return;
        }

        var projects = await _catalog.ListAsync(
            DoltConnectionSettings.FromConfig(_session.Config),
            CancellationToken.None);

        if (projects.Count == 0)
        {
            SetProjects([], null);
            return;
        }

        var aggregate = _session.Config.DoltAggregateAll;
        ProjectInfo current;

        if (aggregate)
        {
            current = ProjectInfo.Aggregate(projects.Sum(static project => project.BeadCount));
        }
        else
        {
            var matched = projects.FirstOrDefault(project =>
                string.Equals(project.Database, _session.Config.DoltDatabase, StringComparison.OrdinalIgnoreCase));

            if (matched is null)
            {
                // 配置里的库在服务器上已不存在：回退到第一个可用项目并提示。
                matched = projects[0];
                _toast.Warning($"项目 {_session.Config.DoltDatabase} 不可用，已回退到 {matched.Database}");
                await _session.ApplyAsync(
                    _session.Config with { DoltDatabase = matched.Database, DoltAggregateAll = false },
                    CancellationToken.None);
                await RefreshAllSafeAsync();
            }

            current = matched;
        }

        SetProjects(BuildProjectList(projects, aggregate), current);
    }

    /// <summary>多项目时在列表最前面插入「全部项目」聚合项。</summary>
    private static IReadOnlyList<ProjectInfo> BuildProjectList(IReadOnlyList<ProjectInfo> projects, bool aggregate) =>
        aggregate || projects.Count > 1
            ? [ProjectInfo.Aggregate(projects.Sum(static project => project.BeadCount)), .. projects]
            : projects;

    private async Task ReloadProjectsSafeAsync()
    {
        try
        {
            await ReloadProjectsAsync();
        }
        catch (Exception ex)
        {
            SetProjects([], null);
            _toast.Handle(ex);
        }
    }

    private void SetProjects(IReadOnlyList<ProjectInfo> projects, ProjectInfo? current)
    {
        _suppressProjectSwitch = true;
        try
        {
            Projects = projects;
            CurrentProject = current;
            IsProjectSwitcherVisible = projects.Count > 0;
        }
        finally
        {
            _suppressProjectSwitch = false;
        }
    }

    private async Task SwitchProjectSafeAsync(ProjectInfo project)
    {
        var previous = _session.Config;
        if (IsCurrentProject(project))
        {
            return;
        }

        var next = project.IsAggregate
            ? previous with { DoltAggregateAll = true }
            : previous with { DoltAggregateAll = false, DoltDatabase = project.Database };

        IsBusy = true;
        StatusText = project.IsAggregate ? "正在加载全部项目..." : $"正在切换到 {project.Database}...";
        try
        {
            await _session.ApplyAsync(next, CancellationToken.None);
            Detail.Close();
            IsDetailOpen = false;
            VersionText = await ReadVersionAsync(CancellationToken.None);
            await RefreshAllAsync(CancellationToken.None);
            _toast.Success(project.IsAggregate ? "已切换到全部项目" : $"已切换到 {project.Database}");
        }
        catch (Exception ex)
        {
            _toast.Handle(ex);
            await RevertProjectAsync(previous, project);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>切换失败时把配置与下拉都退回上一个可用项目。</summary>
    private async Task RevertProjectAsync(AppConfig previous, ProjectInfo failed)
    {
        try
        {
            await _session.ApplyAsync(previous, CancellationToken.None);
            await RefreshAllSafeAsync();
        }
        catch (Exception ex)
        {
            _toast.Handle(ex);
        }

        var restored = previous.DoltAggregateAll
            ? Projects.FirstOrDefault(static candidate => candidate.IsAggregate)
            : Projects.FirstOrDefault(candidate =>
                !candidate.IsAggregate
                && string.Equals(candidate.Database, previous.DoltDatabase, StringComparison.OrdinalIgnoreCase));

        _suppressProjectSwitch = true;
        try
        {
            CurrentProject = restored;
        }
        finally
        {
            _suppressProjectSwitch = false;
        }

        _toast.Warning($"无法切换到 {failed.Label}，已退回上一个项目");
    }

    private void UpdateStatus()
    {
        var at = _coordinator.LastRefreshedAt?.ToLocalTime().ToString("HH:mm:ss") ?? "—";
        var suffix = _isDemo ? " · 演示" : IsReadOnly ? " · 只读" : string.Empty;
        StatusText = $"刷新 {at} · {Board.TotalCount} 项{suffix}";
    }
}
