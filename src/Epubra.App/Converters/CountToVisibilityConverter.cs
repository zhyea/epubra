using System.Collections;
using System.Globalization;
using System.Windows.Data;
using System.Windows;

namespace Epubra.App.Converters;

/// <summary>
/// 集合数量 → Visibility 转换器。
/// count &gt; 0 → Visible；否则 Collapsed。
/// 传入 ConverterParameter="Inverse" 时反转（用于空状态展示）。
/// </summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        int count = value switch
        {
            int i => i,
            ICollection c => c.Count,
            _ => 0
        };
        bool hasItems = count > 0;
        bool inverse = parameter is string s && s.Equals("Inverse", StringComparison.OrdinalIgnoreCase);
        bool visible = inverse ? !hasItems : hasItems;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
