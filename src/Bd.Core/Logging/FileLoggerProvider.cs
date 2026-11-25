using System.Text;
using Bd.Core.Config;
using Microsoft.Extensions.Logging;

namespace Bd.Core.Logging;

/// <summary>日志落盘参数；大小与保留天数来自配置，目录固定为 AppData/Bd.Ui/logs。</summary>
public sealed record FileLoggerOptions
{
    public string Directory { get; init; } = DefaultDirectory();

    public string FileName { get; init; } = "bd-ui.log";

    /// <summary>单文件上限，超过后滚动为带时间戳的归档文件。</summary>
    public long MaxFileBytes { get; init; } = 2L * 1024 * 1024;

    public int RetentionDays { get; init; } = 7;

    public LogLevel MinimumLevel { get; init; } = LogLevel.Information;

    public static string DefaultDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Bd.Ui",
        "logs");

    public static FileLoggerOptions FromConfig(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var normalized = config.Normalize();
        return new FileLoggerOptions
        {
            MaxFileBytes = normalized.LogMaxFileSizeKb * 1024L,
            RetentionDays = normalized.LogRetentionDays
        };
    }
}

/// <summary>
/// 纯文件日志提供器：按大小滚动、按天数清理归档，写入失败一律吞掉，
/// 保证日志本身不会把主流程带崩。
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly FileLogWriter _writer;
    private readonly LogLevel _minimumLevel;
    private bool _disposed;

    public FileLoggerProvider(FileLoggerOptions? options = null)
    {
        var effective = options ?? new FileLoggerOptions();
        _writer = new FileLogWriter(effective);
        _minimumLevel = effective.MinimumLevel;
    }

    public string LogDirectory => _writer.LogDirectory;

    public string CurrentFilePath => _writer.CurrentFilePath;

    public ILogger CreateLogger(string categoryName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new FileLogger(_writer, categoryName, _minimumLevel);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _writer.Dispose();
    }

    private sealed class FileLogger(FileLogWriter writer, string category, LogLevel minimumLevel) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel != LogLevel.None && logLevel >= minimumLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel) || formatter is null)
            {
                return;
            }

            writer.Write(logLevel, category, formatter(state, exception), exception);
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}

/// <summary>串行写入单个当前日志文件，达到上限后改名归档并清理过期归档。</summary>
internal sealed class FileLogWriter : IDisposable
{
    private readonly object _sync = new();
    private readonly FileLoggerOptions _options;
    private StreamWriter? _writer;

    public FileLogWriter(FileLoggerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        CleanupExpiredArchives();
    }

    public string LogDirectory => _options.Directory;

    public string CurrentFilePath => Path.Combine(_options.Directory, _options.FileName);

    public void Write(LogLevel level, string category, string message, Exception? exception)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{ShortLevel(level)}] {category}: {message}";

        lock (_sync)
        {
            try
            {
                EnsureWriter();
                _writer!.WriteLine(line);
                if (exception is not null)
                {
                    _writer.WriteLine(exception.ToString());
                }

                if (_writer.BaseStream.Length >= _options.MaxFileBytes)
                {
                    Rotate();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // 磁盘满、权限不足等情况下静默降级，不影响应用运行。
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private void EnsureWriter()
    {
        if (_writer is not null)
        {
            return;
        }

        System.IO.Directory.CreateDirectory(_options.Directory);
        var stream = new FileStream(CurrentFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true
        };
    }

    private void Rotate()
    {
        _writer?.Dispose();
        _writer = null;

        var stem = Path.GetFileNameWithoutExtension(_options.FileName);
        var extension = Path.GetExtension(_options.FileName);
        var archive = Path.Combine(
            _options.Directory,
            $"{stem}-{DateTimeOffset.Now:yyyyMMdd-HHmmssfff}{extension}");

        File.Move(CurrentFilePath, archive, overwrite: true);
        CleanupExpiredArchives();
    }

    private void CleanupExpiredArchives()
    {
        try
        {
            if (!System.IO.Directory.Exists(_options.Directory))
            {
                return;
            }

            var stem = Path.GetFileNameWithoutExtension(_options.FileName);
            var extension = Path.GetExtension(_options.FileName);
            var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, _options.RetentionDays));

            foreach (var path in System.IO.Directory.EnumerateFiles(_options.Directory, $"{stem}-*{extension}"))
            {
                if (File.GetLastWriteTimeUtc(path) < cutoff)
                {
                    File.Delete(path);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 清理失败留给下次启动。
        }
    }

    private static string ShortLevel(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "UNK"
    };
}
