using System.Windows;

namespace SimulatorApp.Slave.Views;

public partial class ExternalWriteLogWindow : Window
{
    public event Action? ClearRequested;

    public ExternalWriteLogWindow()
    {
        InitializeComponent();
    }

    public void SetLines(IReadOnlyList<string> lines)
    {
        LogTextBox.Text = string.Join(Environment.NewLine, lines);
        LogTextBox.CaretIndex = LogTextBox.Text.Length;
        LogTextBox.ScrollToEnd();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        ClearRequested?.Invoke();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
