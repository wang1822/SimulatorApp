using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace SimulatorApp.Shared.Views;

/// <summary>
/// 与应用深色主题一致的消息框，替代系统 MessageBox.Show。
/// </summary>
public partial class ThemedMessageBox : Window
{
    private ThemedMessageBox(string message, string title, MessageBoxImage icon)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        ApplyIcon(icon);
        Loaded += (_, _) => OkButton.Focus();
    }

    private void ApplyIcon(MessageBoxImage icon)
    {
        switch (icon)
        {
            case MessageBoxImage.Error:
                IconEllipse.Fill = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)); // #EF4444
                IconChar.Text    = "✕";
                IconChar.FontSize = 15;
                break;
            case MessageBoxImage.Warning:
                IconEllipse.Fill = new SolidColorBrush(Color.FromRgb(0xF9, 0x73, 0x16)); // #F97316
                IconChar.Text    = "!";
                break;
            case MessageBoxImage.Question:
                IconEllipse.Fill = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)); // #22C55E
                IconChar.Text    = "?";
                break;
            default: // Information / None
                IconEllipse.Fill = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)); // #3B82F6
                IconChar.Text    = "i";
                break;
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Return or Key.Escape)
            DialogResult = true;
    }

    // ---------------------------------------------------------------
    // 静态工厂方法，与 MessageBox.Show 签名完全兼容
    // ---------------------------------------------------------------

    /// <summary>
    /// 显示深色主题消息框，签名与 <see cref="MessageBox.Show"/> 兼容。
    /// </summary>
    public static MessageBoxResult Show(
        string message,
        string title   = "提示",
        MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage icon     = MessageBoxImage.Information)
    {
        // 找到当前激活窗口作为 Owner，保证居中对齐
        var owner = Application.Current?.Windows
            .OfType<Window>()
            .FirstOrDefault(w => w.IsActive)
            ?? Application.Current?.MainWindow;

        var dialog = new ThemedMessageBox(message, title, icon) { Owner = owner };
        dialog.ShowDialog();
        return MessageBoxResult.OK;
    }
}
