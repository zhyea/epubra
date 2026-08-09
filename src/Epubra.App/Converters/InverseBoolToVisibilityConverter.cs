using System.Globalization;
using System.Windows.Data;
using System.Windows;

namespace Epubra.App.Converters;

/// <summary>
/// 布尔值到 Visibility 的反转转换器。
/// true → Collapsed, false → Visible。
/// 用于"编辑模式时显示编辑器、预览模式时隐藏编辑器"这类场景。
/// </summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && b ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Visibility v && v == Visibility.Collapsed;
    }
}
