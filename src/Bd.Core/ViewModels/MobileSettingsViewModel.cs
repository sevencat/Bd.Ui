using Bd.Core.Config;
using Bd.Core.Dolt;
using Bd.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bd.Core.ViewModels;

/// <summary>
/// 手机端设置页：只暴露连库需要的字段（主机/端口/账号/库/TLS/只读/演示），
/// 主题、人类身份、日志这些桌面向的项在手机上没什么用，不放进来。
/// 保存走 <see cref="AppSession.ApplyAsync"/>，与桌面端共用同一份 config.json 结构。
/// </summary>
public sealed partial class MobileSettingsViewModel : ObservableObject
{
    private readonly AppSession _session;
    private readonly IAppConfigStore _configStore;
    private readonly DoltConnectionFactory _dolt;
    private readonly ToastService _toast;

    public MobileSettingsViewModel(
        AppSession session,
        IAppConfigStore configStore,
        DoltConnectionFactory dolt,
        ToastService toast)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        _dolt = dolt ?? throw new ArgumentNullException(nameof(dolt));
        _toast = toast ?? throw new ArgumentNullException(nameof(toast));
        ReloadFromSession();
    }

    /// <summary>手机上实际写入的配置文件路径（FilesDir 下），方便排查。</summary>
    public string ConfigPath => _configStore.FilePath;

    /// <summary>保存并让运行时换成新配置后触发，外壳据此重新拉数据。</summary>
    public event EventHandler? Saved;

    [ObservableProperty]
    private string _doltHost = DoltDefaults.Host;

    [ObservableProperty]
    private string _doltPortText = DoltDefaults.Port.ToString();

    [ObservableProperty]
    private string _doltUser = DoltDefaults.User;

    [ObservableProperty]
    private string _doltPassword = DoltDefaults.Password;

    [ObservableProperty]
    private string _doltDatabase = DoltDefaults.Database;

    [ObservableProperty]
    private bool _doltUseTls;

    [ObservableProperty]
    private bool _doltAggregateAll;

    [ObservableProperty]
    private bool _readOnly;

    [ObservableProperty]
    private bool _demoMode;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>每次进设置页都从当前生效配置刷新一遍，避免显示过期值。</summary>
    public void ReloadFromSession()
    {
        var config = _session.Config;
        DoltHost = config.DoltHost;
        DoltPortText = config.DoltPort.ToString();
        DoltUser = config.DoltUser;
        DoltPassword = config.DoltPassword ?? string.Empty;
        DoltDatabase = config.DoltDatabase;
        DoltUseTls = config.DoltUseTls;
        DoltAggregateAll = config.DoltAggregateAll;
        ReadOnly = config.ReadOnly;
        DemoMode = config.DemoMode;
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (!TryParseDoltPort(out var port))
        {
            StatusMessage = "Dolt 端口必须是 1-65535 之间的整数";
            _toast.Warning(StatusMessage);
            return;
        }

        IsBusy = true;
        try
        {
            // 以当前配置为底，未在手机界面上暴露的字段（日志上限等）不会被打回默认值。
            var config = _session.Config with
            {
                DoltHost = string.IsNullOrWhiteSpace(DoltHost) ? DoltDefaults.Host : DoltHost.Trim(),
                DoltPort = port,
                DoltUser = string.IsNullOrWhiteSpace(DoltUser) ? DoltDefaults.User : DoltUser.Trim(),
                DoltPassword = string.IsNullOrWhiteSpace(DoltPassword) ? null : DoltPassword.Trim(),
                DoltDatabase = string.IsNullOrWhiteSpace(DoltDatabase) ? DoltDefaults.Database : DoltDatabase.Trim(),
                DoltUseTls = DoltUseTls,
                DoltAggregateAll = DoltAggregateAll,
                ReadOnly = ReadOnly,
                DemoMode = DemoMode
            };

            await _session.ApplyAsync(config, CancellationToken.None);
            ReloadFromSession();
            StatusMessage = $"已保存到 {ConfigPath}";
            _toast.Success("设置已保存，正在重新连接");
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StatusMessage = _toast.Error(ex);
            _toast.Handle(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>按当前输入（不一定已保存）探一次连接，方便先试后存。</summary>
    [RelayCommand]
    public async Task TestConnectionAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var probe = await _dolt.TestAsync(BuildDoltSettings(), CancellationToken.None);
            StatusMessage = probe.Message;
            if (probe.Ok)
            {
                _toast.Success("Dolt 连接正常");
            }
            else
            {
                _toast.Warning("Dolt 连接失败");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = _toast.Error(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private DoltConnectionSettings BuildDoltSettings() => new()
    {
        Host = string.IsNullOrWhiteSpace(DoltHost) ? DoltDefaults.Host : DoltHost.Trim(),
        Port = TryParseDoltPort(out var port) ? port : DoltDefaults.Port,
        User = string.IsNullOrWhiteSpace(DoltUser) ? DoltDefaults.User : DoltUser.Trim(),
        // 留空表示交给环境变量 BEADS_DOLT_PASSWORD。
        Password = string.IsNullOrWhiteSpace(DoltPassword) ? null : DoltPassword.Trim(),
        Database = string.IsNullOrWhiteSpace(DoltDatabase) ? DoltDefaults.Database : DoltDatabase.Trim(),
        UseTls = DoltUseTls
    };

    private bool TryParseDoltPort(out int port) =>
        int.TryParse(DoltPortText?.Trim(), out port) && port is >= 1 and <= 65_535;
}
