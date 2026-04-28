using System.Globalization;
using System.Windows.Data;

namespace SimulatorApp.Shared.Converters;

/// <summary>
/// bool → string 转换器。
/// ConverterParameter 格式："true文本|false文本"，例如 "运行中|已停止"
/// </summary>
[ValueConversion(typeof(bool), typeof(string))]
public class BoolToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool flag = value is bool b && b;
        if (parameter is string param)
        {
            var parts = param.Split('|');
            if (parts.Length == 2)
                return flag ? parts[0] : parts[1];
        }
        return flag ? "是" : "否";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
