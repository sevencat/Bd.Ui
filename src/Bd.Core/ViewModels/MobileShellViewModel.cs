using Bd.Core.Abstractions;
using Bd.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bd.Core.ViewModels;

/// <summary>手机端当前显示哪一屏。</summary>
public enum MobileScreen
{
    /// <summary>任务树列表（默认屏）。</summary>
    List,

    /// <summary>已点开某条任务，正在拉详情。</summary>
    Loading,

    /// <summary>详情页。</summary>
    Detail,

    /// <summary>连接设置页。</summary>
    Settings
}

/// <summary>
/// 手机端外壳：一屏只显示一个页面——任务树列表 / loading / 详情页，靠返回回到列表。
/// 与桌面端 <see cref="ShellViewModel"/> 相互独立：没有左右分栏，也不复用桌面视图。
/// </summary>
public sealed partial class MobileShellViewModel : ObservableObject
{
    private readonly AppSession _session;
    private readonly StoreProvider _store;
    private readonly ToastService _toast;

    public MobileShellViewModel(
        BoardViewModel board,
        BeadDetailViewModel detail,
        MobileSettingsViewModel settings,
        AppSession session,
        StoreProvider store,
        ToastService toast)
    {
        Board = board ?? throw new ArgumentNullException(nameof(board));
        Detail = detail ?? throw new ArgumentNullException(nameof(detail));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _toast = toast ?? throw new ArgumentNullException(nameof(toast));

        Board.Updated += (_, _) => UpdateStatus();

        // 详情页里的父/子任务跳转：留在详情屏，只是重新走一次 loading。
        Detail.NavigateRequested += (_, id) => _ = OpenDetailSafeAsync(id);
        // 归档/删除后详情自己会 Close，手机端随之退回列表。
        Detail.Closed += (_, _) => GoBack();
        // 写操作成功后刷新列表，保证返回时看到的是新状态。
        Detail.Changed += (_, _) => _ = RefreshAfterWriteAsync();
        // 设置保存后运行时已经换上新 store，回列表重新拉一遍。
        Settings.Saved += (_, _) => _ = ReconnectAsync();
    }

    public BoardViewModel Board { get; }

    public BeadDetailViewModel Detail { get; }

    public MobileSettingsViewModel Settings { get; }

    public ToastService Toasts => _toast;

    [ObservableProperty]
    private MobileScreen _screen = MobileScreen.List;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isInitialized;

    [ObservableProperty]
    private string _statusText = "正在连接...";

    [ObservableProperty]
    private string _versionText = string.Empty;

    /// <summary>loading 屏上的说明文字，带上正在打开的任务 ID。</summary>
    [ObservableProperty]
    private string _loadingText = "正在加载...";

    /// <summary>连接或刷新失败时的说明，成功时清空。</summary>
    [ObservableProperty]
    private string? _errorMessage;

    public bool IsListVisible => Screen == MobileScreen.List;

    public bool IsLoadingVisible => Screen == MobileScreen.Loading;

    public bool IsDetailVisible => Screen == MobileScreen.Detail;

    public bool IsSettingsVisible => Screen == MobileScreen.Settings;

    /// <summary>不在列表屏时，Android 返回键应先回列表而不是退出应用。</summary>
    public bool CanGoBack => Screen != MobileScreen.List;

    partial void OnScreenChanged(MobileScreen value)
    {
        OnPropertyChanged(nameof(IsListVisible));
        OnPropertyChanged(nameof(IsLoadingVisible));
        OnPropertyChanged(nameof(IsDetailVisible));
        OnPropertyChanged(nameof(IsSettingsVisible));
        OnPropertyChanged(nameof(CanGoBack));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            IsBusy = true;
            StatusText = "正在连接 Dolt...";
            await _session.InitializeAsync(cancellationToken);
            await Board.RefreshCoreAsync(cancellationToken, silent: true);
            VersionText = await ReadVersionAsync(cancellationToken);
            ErrorMessage = Board.ErrorMessage;
            IsInitialized = true;
            UpdateStatus();
        }
        catch (Exception ex)
        {
            ErrorMessage = _toast.Error(ex);
            StatusText = "初始化失败，请检查设置";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>下拉/按钮刷新：重新拉取任务树；筛选范围沿用看板当前设置。</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (!_store.IsConfigured)
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusText = "正在刷新...";
            await Board.RefreshCoreAsync(CancellationToken.None, silent: false);
            ErrorMessage = Board.ErrorMessage;
            UpdateStatus();
        }
        catch (Exception ex)
        {
            ErrorMessage = _toast.Error(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>点选树上的任务：先进 loading 屏，拉完再切到详情页。</summary>
    [RelayCommand]
    public async Task OpenRowAsync(BoardTreeNodeViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        await OpenDetailSafeAsync(row.Card.Id);
    }

    /// <summary>返回列表；已在列表时不做事（交给系统退出应用）。</summary>
    [RelayCommand]
    public void GoBack()
    {
        if (Screen == MobileScreen.List)
        {
            return;
        }

        Screen = MobileScreen.List;
        Board.ClearSelection();
    }

    /// <summary>进设置页：先把界面上的值对齐当前生效配置。</summary>
    [RelayCommand]
    public void OpenSettings()
    {
        Settings.ReloadFromSession();
        Settings.StatusMessage = string.Empty;
        Screen = MobileScreen.Settings;
    }

    public async Task OpenDetailAsync(string id)
    {
        // 先清场再切屏，避免详情页闪一下上一条任务的内容。
        Detail.BeginLoading(id);
        LoadingText = $"正在加载 {id}";
        Screen = MobileScreen.Loading;

        await Detail.LoadAsync(id);

        if (Detail.IsOpen)
        {
            Screen = MobileScreen.Detail;
            return;
        }

        // 拉取失败时 LoadAsync 已经把原因塞进 toast，手机端改成列表上方的错误条。
        Screen = MobileScreen.List;
        ErrorMessage = _toast.Items.Count > 0 ? _toast.Items[^1].Text : "加载详情失败";
    }

    private async Task OpenDetailSafeAsync(string id)
    {
        try
        {
            await OpenDetailAsync(id);
        }
        catch (Exception ex)
        {
            Screen = MobileScreen.List;
            ErrorMessage = _toast.Error(ex);
        }
    }

    /// <summary>设置保存后：运行时 store 已被换新，回列表重新拉一次数据并刷新版本号。</summary>
    private async Task ReconnectAsync()
    {
        Screen = MobileScreen.List;
        try
        {
            IsBusy = true;
            StatusText = "正在重新连接...";
            await Board.RefreshCoreAsync(CancellationToken.None, silent: true);
            VersionText = await ReadVersionAsync(CancellationToken.None);
            ErrorMessage = Board.ErrorMessage;
            IsInitialized = true;
            UpdateStatus();
        }
        catch (Exception ex)
        {
            ErrorMessage = _toast.Error(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshAfterWriteAsync()
    {
        try
        {
            await Board.RefreshCoreAsync(CancellationToken.None, silent: true);
            UpdateStatus();
        }
        catch (Exception ex)
        {
            ErrorMessage = _toast.Error(ex);
        }
    }

    private async Task<string> ReadVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            return $"dolt {await _store.GetVersionAsync(cancellationToken)}";
        }
        catch (Exception ex)
        {
            return _toast.Error(ex);
        }
    }

    private void UpdateStatus() =>
        StatusText = Board.TotalCount == 0 ? "暂无任务" : $"共 {Board.TotalCount} 项 · 上次刷新 {Board.LastLoadedText}";
}
