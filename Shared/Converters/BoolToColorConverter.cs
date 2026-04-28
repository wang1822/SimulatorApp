using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SimulatorApp.Shared.Converters;

/// <summary>
/// bool → Brush 转换器。
/// 用于状态指示灯：true=绿色（运行中），false=灰色（停止）。
/// 可通过 ConverterParameter 自定义颜色，格式："运行色|停止色"（十六进制）
/// </summary>
[ValueConversion(typeof(bool), typeof(Brush))]
public class BoolToColorConverter : IValueConverter
{
    private static readonly Brush RunningBrush = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)); // #22C55E 绿
    private static readonly Brush StoppedBrush  = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)); // #64748B 灰

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isRunning = value is bool b && b;

        if (parameter is string param)
        {
            var parts = param.Split('|');
            if (parts.Length == 2)
            {
                try
                {
                    var c1 = (Color)ColorConverter.ConvertFromString(parts[0]);
                    var c2 = (Color)ColorConverter.ConvertFromString(parts[1]);
                    return isRunning
                        ? new SolidColorBrush(c1)
                        : new SolidColorBrush(c2);
                }
                catch { /* 参数格式错误，降级到默认色 */ }
            }
        }

        return isRunning ? RunningBrush : StoppedBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
