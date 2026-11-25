using System.Reflection;
using Bd.Core.Config;
using Bd.Core.Logging;
using Bd.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bd.Core.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSession _session;
    private readonly DoctorService _doctor;
    private readonly ToastService _toast;
    private bool _suppress;

    public SettingsViewModel(AppSession session, DoctorService doctor, ToastService toast)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _doctor = doctor ?? throw new ArgumentNullException(nameof(doctor));
        _toast = toast ?? throw new ArgumentNullException(nameof(toast));
        ReloadFromSession();
    }

    public IReadOnlyList<string> Themes { get; } = ["Dark", "Light"];

    public string ConfigPath => JsonAppConfigStore.DefaultFilePath();

    /// <summary>应用版本（来自程序集的 InformationalVersion，由 set-version.ps1 写入）。</summary>
    public string AppVersion { get; } = BuildAppVersion();

    public string LogDirectory => FileLoggerOptions.DefaultDirectory();

    public event EventHandler? Applied;

    [ObservableProperty]
    private string _repoPath = string.Empty;

    [ObservableProperty]
    private string _bdBinaryPath = "bd";

    [ObservableProperty]
    private string _humanActor = string.Empty;

    [ObservableProperty]
    private string _humanAllowlistText = string.Empty;

    [ObservableProperty]
    private double _pollSeconds = 5;

    [ObservableProperty]
    private string _selectedTheme = "Dark";

    [ObservableProperty]
    private bool _readOnly;

    [ObservableProperty]
    private bool _demoMode;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _saveStatus = string.Empty;

    [ObservableProperty]
    private string _doctorReport = string.Empty;

    public void ReloadFromSession()
    {
        _suppress = true;
        try
        {
            var config = _session.Config;
            RepoPath = config.RepoPath ?? string.Empty;
            BdBinaryPath = config.BdBinaryPath;
            HumanActor = config.HumanActor ?? string.Empty;
            HumanAllowlistText = string.Join(", ", config.HumanAllowlist);
            PollSeconds = config.PollSeconds;
            SelectedTheme = config.Theme;
            ReadOnly = config.ReadOnly;
            DemoMode = config.DemoMode;
        }
        finally
        {
            _suppress = false;
        }
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (_suppress || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            // 以当前配置为基础，避免未在界面上暴露的字段（日志上限等）被保存时重置。
            var config = _session.Config with
            {
                RepoPath = string.IsNullOrWhiteSpace(RepoPath) ? null : RepoPath.Trim(),
                BdBinaryPath = BdBinaryPath,
                HumanActor = string.IsNullOrWhiteSpace(HumanActor) ? null : HumanActor.Trim(),
                HumanAllowlist = HumanAllowlistText
                    .Split([',', '，', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                PollSeconds = (int)Math.Round(PollSeconds),
                Theme = SelectedTheme,
                ReadOnly = ReadOnly,
                DemoMode = DemoMode,
                StorageBackend = "cli"
            };

            await _session.ApplyAsync(config, CancellationToken.None);
            ReloadFromSession();
            SaveStatus = $"已保存到 {ConfigPath}";
            _toast.Success("设置已保存并生效");
            Applied?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            SaveStatus = "保存失败";
            _toast.Handle(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task RunDoctorAsync()
    {
        IsBusy = true;
        try
        {
            var report = await _doctor.CheckAsync(
                string.IsNullOrWhiteSpace(RepoPath) ? null : RepoPath.Trim(),
                CancellationToken.None);
            var lines = new List<string>
            {
                $"状态：{(report.Ok ? "正常" : "异常")}",
                $"bd 版本：{report.BdVersion}",
                $"仓库：{report.RepoPath}",
                $"schema：{report.SchemaVersion}",
                $"摘要：{report.Summary}"
            };
            lines.AddRange(report.Issues.Select(static issue => $"问题：{issue}"));
            DoctorReport = string.Join(Environment.NewLine, lines);
            if (report.Ok)
            {
                _toast.Success("自检通过");
            }
            else
            {
                _toast.Warning("自检发现问题，详见报告");
            }
        }
        catch (Exception ex)
        {
            DoctorReport = _toast.Error(ex);
            _toast.Handle(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void Reload()
    {
        ReloadFromSession();
        SaveStatus = "已还原为当前生效配置";
    }

    private static string BuildAppVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(SettingsViewModel).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = informational ?? assembly.GetName().Version?.ToString(3) ?? "unknown";
        var metadata = version.IndexOf('+');
        if (metadata >= 0)
        {
            version = version[..metadata];
        }

        return $"Bd.Ui {version}";
    }
}
