namespace Bd.Core.BdCli;

public enum BdErrorCode
{
    Unknown,
    NotFound,
    InvalidArgument,
    LockBusy,
    NotARepo,
    BinaryMissing,
    SchemaMismatch,
    Timeout,
    Canceled,
    ReadOnly
}

public sealed class BdException : Exception
{
    public BdException(
        BdErrorCode code,
        string userMessage,
        string? command = null,
        int? exitCode = null,
        string? stdErr = null,
        string? actor = null,
        Exception? inner = null)
        : base(userMessage, inner)
    {
        Code = code;
        Command = command;
        ExitCode = exitCode;
        StdErr = stdErr;
        Actor = actor;
        UserMessage = userMessage;
    }

    public BdErrorCode Code { get; }

    public string? Command { get; }

    public int? ExitCode { get; }

    public string? StdErr { get; }

    public string? Actor { get; }

    public string UserMessage { get; }

    public static BdException NotFound(string id) =>
        new(BdErrorCode.NotFound, $"找不到任务 {id}", $"show {id}");

    public static BdException ReadOnly(string? message = null) =>
        new(BdErrorCode.ReadOnly, message ?? "只读模式：当前配置禁止写操作");

    public static BdException FromResult(BdProcessResult result, string command, string? actor = null)
    {
        if (result.TimedOut)
        {
            return new BdException(BdErrorCode.Timeout, "bd 命令超时，请检查仓库是否被其它进程占用", command, result.ExitCode, result.StdErr, actor);
        }

        var code = Map(result.StdErr, result.StdOut);
        var message = code switch
        {
            BdErrorCode.NotFound => "找不到指定的任务",
            BdErrorCode.LockBusy => "仓库正在被其它进程写入，请稍后重试",
            BdErrorCode.NotARepo => "当前目录不是 beads 仓库（缺少 .beads）",
            BdErrorCode.BinaryMissing => "找不到 bd 可执行文件，请在设置中指定路径",
            BdErrorCode.SchemaMismatch => "beads 数据库版本与当前 bd 不兼容",
            BdErrorCode.InvalidArgument => "命令参数不合法",
            _ => "bd 命令执行失败"
        };

        var detail = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
        if (!string.IsNullOrWhiteSpace(detail))
        {
            message = $"{message}：{FirstLine(detail)}";
        }

        return new BdException(code, message, command, result.ExitCode, result.StdErr, actor);
    }

    public static BdErrorCode Map(string? stdErr, string? stdOut = null)
    {
        var text = $"{stdErr}\n{stdOut}".ToLowerInvariant();

        if (text.Contains("no issues found", StringComparison.Ordinal) || text.Contains("not found", StringComparison.Ordinal))
        {
            return BdErrorCode.NotFound;
        }

        if (text.Contains("locked", StringComparison.Ordinal) || text.Contains("busy", StringComparison.Ordinal) || text.Contains("conflict", StringComparison.Ordinal))
        {
            return BdErrorCode.LockBusy;
        }

        if (text.Contains("schema", StringComparison.Ordinal))
        {
            return BdErrorCode.SchemaMismatch;
        }

        if (text.Contains(".beads", StringComparison.Ordinal) && (text.Contains("not a", StringComparison.Ordinal) || text.Contains("no ", StringComparison.Ordinal)))
        {
            return BdErrorCode.NotARepo;
        }

        if (text.Contains("executable file not found", StringComparison.Ordinal)
            || text.Contains("cannot find the file", StringComparison.Ordinal)
            || text.Contains("no such file", StringComparison.Ordinal)
            || text.Contains("not recognized", StringComparison.Ordinal))
        {
            return BdErrorCode.BinaryMissing;
        }

        if (text.Contains("unknown flag", StringComparison.Ordinal) || text.Contains("invalid argument", StringComparison.Ordinal) || text.Contains("invalid value", StringComparison.Ordinal))
        {
            return BdErrorCode.InvalidArgument;
        }

        return BdErrorCode.Unknown;
    }

    private static string FirstLine(string text)
    {
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return line is null or "" ? text.Trim() : line;
    }
}
