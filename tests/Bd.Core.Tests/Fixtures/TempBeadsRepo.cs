using System.ComponentModel;
using Bd.Core.BdCli;
using Xunit;

namespace Bd.Core.Tests.Fixtures;

/// <summary>
/// 在系统临时目录里用 <c>bd init</c> 建真实 .beads 仓库，供合同测试与并发测试使用。
/// 每个实例使用唯一目录，因此多个类/多个测试可以并行且互不干扰。
/// </summary>
public sealed class TempBeadsRepo : IDisposable
{
    public const string DefaultPrefix = "bdtest";

    private const int CleanupAttempts = 5;

    private static readonly Lazy<string?> Probe = new(ProbeBdVersion);

    public TempBeadsRepo() : this(DefaultPrefix)
    {
    }

    private TempBeadsRepo(string prefix)
    {
        RootPath = Path.Combine(Path.GetTempPath(), "bd-ui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);

        var runner = new BdProcessRunner();
        var init = runner.RunAsync(
            new BdProcessRequest
            {
                Arguments = ["init", "--non-interactive", "--prefix", prefix],
                WorkingDirectory = RootPath,
                Timeout = TimeSpan.FromMinutes(3)
            },
            CancellationToken.None).GetAwaiter().GetResult();

        if (!init.Succeeded)
        {
            TryDelete(RootPath);
            throw new InvalidOperationException($"bd init 失败（{init.ExitCode}）：{init.StdErr}{init.StdOut}");
        }

        Store = new BdCliStore(runner, RootPath);
    }

    public string RootPath { get; }

    public BdCliStore Store { get; }

    public static bool BdAvailable => Probe.Value is not null;

    public static string BdVersion => Probe.Value ?? string.Empty;

    public Task<SeedDataResult> SeedAsync(CancellationToken cancellationToken = default) =>
        SeedData.PopulateAsync(Store, cancellationToken);

    public void Dispose() => TryDelete(RootPath);

    private static void TryDelete(string path)
    {
        for (var attempt = 0; attempt < CleanupAttempts; attempt++)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    return;
                }

                ClearReadOnlyAttributes(path);
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(200);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(200);
            }
        }
    }

    private static void ClearReadOnlyAttributes(string path)
    {
        var options = new EnumerationOptions
        {
            AttributesToSkip = 0,
            RecurseSubdirectories = true,
            IgnoreInaccessible = true
        };

        foreach (var file in Directory.EnumerateFiles(path, "*", options))
        {
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static string? ProbeBdVersion()
    {
        try
        {
            var result = new BdProcessRunner().RunAsync(
                new BdProcessRequest
                {
                    Arguments = ["--version"],
                    WorkingDirectory = Path.GetTempPath(),
                    Timeout = TimeSpan.FromSeconds(30)
                },
                CancellationToken.None).GetAwaiter().GetResult();

            return result.Succeeded ? result.StdOut.Trim() : null;
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}

/// <summary>本机缺少 bd CLI 时自动跳过，而不是失败。</summary>
public sealed class BdFactAttribute : FactAttribute
{
    public BdFactAttribute()
    {
        if (!TempBeadsRepo.BdAvailable)
        {
            Skip = "本机未安装 bd CLI，跳过真实 bd 合同测试";
        }
    }
}
