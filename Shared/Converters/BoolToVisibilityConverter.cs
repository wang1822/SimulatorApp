using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SimulatorApp.Shared.Converters;

/// <summary>
/// bool → Visibility 转换器。
/// true → Visible，false → Collapsed（或通过 ConverterParameter="Inverse" 反转）
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool flag = value is bool b && b;
        bool inverse = parameter is string s && s.Equals("Inverse", StringComparison.OrdinalIgnoreCase);
        if (inverse) flag = !flag;
        // 当目标属性是 bool（如 IsEnabled）时直接返回 bool，避免类型不匹配
        if (targetType == typeof(bool)) return flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool inverse = parameter is string s && s.Equals("Inverse", StringComparison.OrdinalIgnoreCase);
        bool visible = value is Visibility v && v == Visibility.Visible;
        return inverse ? !visible : visible;
    }
}
