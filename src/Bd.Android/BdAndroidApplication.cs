using Android.App;
using Android.Runtime;
using Avalonia.Android;

namespace Bd.Mobile;

/// <summary>
/// Avalonia 12 的 Android 启动入口在 Application 上（而不是 Activity）：
/// 由它创建 AppBuilder 与单视图生命周期，Activity 只负责承载视图。
/// </summary>
[Application]
public class BdAndroidApplication : AvaloniaAndroidApplication<App>
{
    public BdAndroidApplication(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }
}
