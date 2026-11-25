using System.Collections.ObjectModel;
using Bd.Core.BdCli;

namespace Bd.Core.Services;

public enum ToastLevel
{
    Success,
    Warning,
    Error
}

public sealed record ToastMessage(Guid Id, ToastLevel Level, string Text, DateTimeOffset CreatedAt)
{
    public string LevelText => Level switch
    {
        ToastLevel.Success => "成功",
        ToastLevel.Warning => "注意",
        _ => "错误"
    };
}

/// <summary>统一提示通道，禁止 MessageBox；最多保留 5 条，UI 负责计时移除。</summary>
public sealed class ToastService
{
    public const int MaxItems = 5;

    public ObservableCollection<ToastMessage> Items { get; } = [];

    public event EventHandler<ToastMessage>? ToastAdded;

    public ToastMessage Show(ToastLevel level, string text)
    {
        var message = new ToastMessage(Guid.NewGuid(), level, text, DateTimeOffset.UtcNow);
        Items.Add(message);
        while (Items.Count > MaxItems)
        {
            Items.RemoveAt(0);
        }

        ToastAdded?.Invoke(this, message);
        return message;
    }

    public void Success(string text) => Show(ToastLevel.Success, text);

    public void Warning(string text) => Show(ToastLevel.Warning, text);

    public void Error(string text) => Show(ToastLevel.Error, text);

    public string Error(Exception exception) =>
        exception is BdException bd ? bd.UserMessage : exception.Message;

    public void Handle(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Error(Error(exception));
    }

    public void Dismiss(ToastMessage message) => Items.Remove(message);

    public void Clear() => Items.Clear();
}
