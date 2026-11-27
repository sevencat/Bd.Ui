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

/// <summary>
/// 环境自检：只连接 Dolt 服务器核对版本、schema 与任务数量，
/// 不再依赖本地 bd 可执行文件或 <c>.beads</c> 目录。
/// </summary>
public sealed class DoctorService
{
    private readonly IBdStore _store;

    public DoctorService(IBdStore store) => _store = store;

    public async Task<DoctorReport> CheckAsync(CancellationToken cancellationToken)
    {
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
                Summary = ex.Message,
                Issues = [ex.Message]
            };
        }

        // 演示数据没有真实库，不做版本/schema 判定。
        if (string.Equals(_store.BackendName, DemoDataProvider.BackendName, StringComparison.Ordinal))
        {
            return report;
        }

        return Evaluate(report);
    }

    /// <summary>把库内自检结果折算成可直接展示的结论：版本缺失、schema 异常都算问题。</summary>
    public static DoctorReport Evaluate(DoctorReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var problems = new List<string>();
        if (BdVersionInfo.TryParse(report.BdVersion) is null)
        {
            problems.Add("数据库缺少 beads 版本记录（local_metadata.bd_version），请确认连接的是 beads 库");
        }

        if (report.SchemaVersion <= 0)
        {
            problems.Add("未读到 schema 版本，库结构可能不完整");
        }

        var issues = report.Issues
            .Concat(problems)
            .Where(static text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        return report with
        {
            Ok = report.Ok && issues.Count == 0,
            Summary = issues.Count == 0
                ? $"Dolt 自检通过（beads {report.BdVersion} · schema {report.SchemaVersion}）"
                : $"发现 {issues.Count} 项问题",
            Issues = issues
        };
    }
}
