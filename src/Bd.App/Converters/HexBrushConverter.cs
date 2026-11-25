using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Bd.App.Converters;

/// <summary>把 ViewModel 中的十六进制色值转成画刷，避免 Core 依赖 Avalonia。</summary>
public sealed class HexBrushConverter : IValueConverter
{
    public static HexBrushConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string text && Color.TryParse(text, out var color)
            ? new SolidColorBrush(color)
            : Brushes.Transparent;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
