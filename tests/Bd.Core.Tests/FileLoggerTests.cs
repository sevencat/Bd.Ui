using Bd.Core.Config;
using Bd.Core.Logging;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Bd.Core.Tests;

public sealed class FileLoggerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bd-ui-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void 日志包含时间级别消息与完整堆栈()
    {
        using var provider = new FileLoggerProvider(new FileLoggerOptions { Directory = _directory });
        var logger = provider.CreateLogger("Bd.Test");
        var exception = new InvalidOperationException("boom");

        logger.LogError(exception, "模拟异常 {Id}", 7);

        var text = ReadLog(provider.CurrentFilePath);
        text.Should().Contain("[ERR]")
            .And.Contain("Bd.Test")
            .And.Contain("模拟异常 7")
            .And.Contain("System.InvalidOperationException")
            .And.Contain("boom");
    }

    [Fact]
    public void 超过大小上限时滚动并保留归档()
    {
        using var provider = new FileLoggerProvider(new FileLoggerOptions
        {
            Directory = _directory,
            MaxFileBytes = 256
        });
        var logger = provider.CreateLogger("Bd.Test");

        for (var i = 0; i < 20; i++)
        {
            logger.LogInformation("滚动 {Index} {Payload}", i, new string('x', 48));
        }

        var files = Directory.GetFiles(_directory, "bd-ui*.log");
        files.Should().HaveCountGreaterThan(1);
        files.Select(ReadLog).Should().Contain(text => text.Contains("滚动 0"));
        File.Exists(provider.CurrentFilePath).Should().BeTrue();
    }

    [Fact]
    public void 启动时清理超过保留天数的归档()
    {
        Directory.CreateDirectory(_directory);
        var expired = Path.Combine(_directory, "bd-ui-20000101-000000.log");
        var fresh = Path.Combine(_directory, "bd-ui-29990101-000000.log");
        File.WriteAllText(expired, "old");
        File.WriteAllText(fresh, "fresh");
        File.SetLastWriteTimeUtc(expired, DateTime.UtcNow.AddDays(-10));

        using var provider = new FileLoggerProvider(new FileLoggerOptions
        {
            Directory = _directory,
            RetentionDays = 3
        });

        File.Exists(expired).Should().BeFalse();
        File.Exists(fresh).Should().BeTrue();
    }

    [Fact]
    public void 低于最小级别的日志不落盘()
    {
        using var provider = new FileLoggerProvider(new FileLoggerOptions
        {
            Directory = _directory,
            MinimumLevel = LogLevel.Warning
        });
        var logger = provider.CreateLogger("Bd.Test");

        logger.LogInformation("仅调试信息");
        logger.LogWarning("需要保留的告警");

        var text = ReadLog(provider.CurrentFilePath);
        text.Should().NotContain("仅调试信息").And.Contain("需要保留的告警");
    }

    [Fact]
    public void 配置到文件日志参数的映射经过归一化()
    {
        var options = FileLoggerOptions.FromConfig(new AppConfig
        {
            LogMaxFileSizeKb = 128,
            LogRetentionDays = 30
        });

        options.MaxFileBytes.Should().Be(128 * 1024);
        options.RetentionDays.Should().Be(30);
        options.Directory.Should().Be(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Bd.Ui",
            "logs"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>日志文件保持写入句柄，读取时必须共享读写。</summary>
    private static string ReadLog(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
