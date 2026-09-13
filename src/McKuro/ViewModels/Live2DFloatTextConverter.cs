using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace McKuro.ViewModels;

/// <summary>
/// Live2D 显示参数 float ↔ 输入框文本(不变区域文化)。
/// 解析失败(输入中/非法)返回 DoNothing:保留用户正在输入的内容,合法时才写回源;
/// 源更新(滑条拖动/首页同步)时以 0.0## 格式回显。
/// </summary>
public sealed class Live2DFloatTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is float f ? f.ToString("0.0##", CultureInfo.InvariantCulture) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value as string;
        if (string.IsNullOrWhiteSpace(text))
        {
            return BindingOperations.DoNothing;
        }
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)
            ? f
            : BindingOperations.DoNothing;
    }
}
