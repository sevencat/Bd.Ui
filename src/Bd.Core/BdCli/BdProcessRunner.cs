using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Bd.Core.BdCli;

/// <summary>直接执行 bd（不经 shell），参数通过 ArgumentList 传递，避免注入与转义问题。</summary>
public sealed class BdProcessRunner : IBdProcessRunner
{
    private readonly string _bdPath;

    public BdProcessRunner(string? bdPath = null) =>
        _bdPath = string.IsNullOrWhiteSpace(bdPath) ? "bd" : bdPath.Trim();

    public async Task<BdProcessResult> RunAsync(BdProcessRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        using var process = new Process { StartInfo = CreateStartInfo(request) };

        try
        {
            if (!process.Start())
            {
                throw new BdException(BdErrorCode.BinaryMissing, $"无法启动 bd：{_bdPath}", request.CommandText, actor: request.Actor);
            }
        }
        catch (Win32Exception ex)
        {
            throw new BdException(BdErrorCode.BinaryMissing, $"找不到 bd 可执行文件：{_bdPath}", request.CommandText, actor: request.Actor, inner: ex);
        }

        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();

        using var timeoutSource = request.Timeout is { } timeout
            ? new CancellationTokenSource(timeout)
            : null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource?.Token ?? CancellationToken.None);

        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return new BdProcessResult
            {
                ExitCode = -1,
                StdOut = await SafeReadAsync(stdOutTask).ConfigureAwait(false),
                StdErr = await SafeReadAsync(stdErrTask).ConfigureAwait(false),
                TimedOut = true
            };
        }

        return new BdProcessResult
        {
            ExitCode = process.ExitCode,
            StdOut = await SafeReadAsync(stdOutTask).ConfigureAwait(false),
            StdErr = await SafeReadAsync(stdErrTask).ConfigureAwait(false)
        };
    }

    private ProcessStartInfo CreateStartInfo(BdProcessRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _bdPath,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["BD_JSON_ENVELOPE"] = "1";
        startInfo.Environment["NO_COLOR"] = "1";
        startInfo.Environment["CLICOLOR"] = "0";

        if (!string.IsNullOrWhiteSpace(request.Actor))
        {
            startInfo.Environment["BEADS_ACTOR"] = request.Actor;
        }

        return startInfo;
    }

    private static async Task<string> SafeReadAsync(Task<string> readTask)
    {
        try
        {
            return await readTask.ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return string.Empty;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // 进程已退出。
        }
        catch (Win32Exception)
        {
            // 无权限，忽略。
        }
    }
}
