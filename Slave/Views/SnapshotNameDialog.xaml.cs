using System.Windows;
using System.Windows.Input;

namespace SimulatorApp.Slave.Views;

public partial class SnapshotNameDialog : Window
{
    public SnapshotNameDialog(string defaultName = "", string title = "保存快照", string prompt = "请输入快照名称")
    {
        InitializeComponent();
        Title = title;
        PromptTextBlock.Text = prompt;
        NameBox.Text = defaultName;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public string SnapshotName => NameBox.Text.Trim();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SnapshotName))
        {
            ErrorTextBlock.Text = "快照名称不能为空。";
            NameBox.Focus();
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void NameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Ok_Click(sender, e);
            e.Handled = true;
        }
    }
}
