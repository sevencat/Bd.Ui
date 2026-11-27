namespace Bd.Core.Abstractions;

public enum BdErrorCode
{
    Unknown = 0,
    NotFound,
    NotBeadsDatabase,
    InvalidArgument,
    LockBusy,
    SchemaMismatch,
    Timeout,
    Canceled,
    ReadOnly,
    Connection
}

/// <summary>
/// 存储层对外抛出的业务异常：只带用户可读信息与错误分类，
/// 上层（Toast / ViewModel）不感知具体存储实现。
/// </summary>
public sealed class BdException : Exception
{
    public BdException(
        BdErrorCode code,
        string userMessage,
        string? actor = null,
        Exception? inner = null)
        : base(userMessage, inner)
    {
        Code = code;
        Actor = actor;
        UserMessage = userMessage;
    }

    public BdErrorCode Code { get; }

    public string? Actor { get; }

    public string UserMessage { get; }

    public static BdException NotFound(string id) =>
        new(BdErrorCode.NotFound, $"找不到任务 {id}");

    public static BdException ReadOnly(string? message = null) =>
        new(BdErrorCode.ReadOnly, message ?? "只读模式：当前配置禁止写操作");
}
