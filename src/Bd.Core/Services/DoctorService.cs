using Bd.Core.Abstractions;

namespace Bd.Core.Services;

public sealed record BdVersionInfo(int Major, int Minor, int Patch)
{
    public static BdVersionInfo? TryParse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var parts = raw.Trim().Split('.', '-', '+');
        if (parts.Length < 2
            || !int.TryParse(parts[0], out var major)
            || !int.TryParse(parts[1], out var minor))
        {
            return null;
        }

        var patch = parts.Length > 2 && int.TryParse(parts[2], out var parsedPatch) ? parsedPatch : 0;
        return new BdVersionInfo(major, minor, patch);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}

public sealed class DoctorService
{
    private static readonly BdVersionInfo MinimumVersion = new(1, 0, 0);
    private readonly IBdStore _store;

    public DoctorService(IBdStore store) => _store = store;

    public async Task<DoctorReport> CheckAsync(string? repoPath, CancellationToken cancellationToken)
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(repoPath))
        {
            problems.Add("尚未配置 beads 仓库路径");
        }
        else if (!Directory.Exists(repoPath))
        {
            problems.Add($"仓库路径不存在：{repoPath}");
        }
        else if (!Directory.Exists(Path.Combine(repoPath, ".beads")))
        {
            problems.Add($"目录中缺少 .beads：{repoPath}");
        }

        DoctorReport report;
        try
        {
            report = await _store.RunDoctorAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            report = new DoctorReport
            {
                Ok = false,
                RepoPath = repoPath ?? string.Empty,
                Summary = ex.Message,
                Issues = [ex.Message]
            };
        }

        var version = BdVersionInfo.TryParse(report.BdVersion);
        if (version is null)
        {
            problems.Add("无法识别 bd 版本，请确认 bd 可执行文件可用");
        }
        else if (version.Major < MinimumVersion.Major)
        {
            problems.Add($"bd 版本过低：{version}，至少需要 {MinimumVersion}");
        }

        var issues = report.Issues.Concat(problems).Where(static text => !string.IsNullOrWhiteSpace(text)).ToList();
        return report with
        {
            Ok = report.Ok && issues.Count == 0,
            RepoPath = repoPath ?? report.RepoPath,
            Summary = issues.Count == 0
                ? $"环境检查通过（bd {report.BdVersion}）"
                : $"发现 {issues.Count} 项问题",
            Issues = issues
        };
    }
}
