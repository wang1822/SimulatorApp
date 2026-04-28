using System.Windows;
using System.Windows.Input;

namespace SimulatorApp.Master.Views;

public partial class PasswordDialog : Window
{
    private readonly string _correct;

    /// <summary>密码验证通过后为 true</summary>
    public bool Confirmed { get; private set; }

    public PasswordDialog(string correct = "dy@123..")
    {
        InitializeComponent();
        _correct = correct;
        Loaded += (_, _) => PasswordBox.Focus();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => TryConfirm();
    private void Cancel_Click(object sender, RoutedEventArgs e)  => Close();

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)  TryConfirm();
        if (e.Key == Key.Escape) Close();
    }

    private void TryConfirm()
    {
        if (PasswordBox.Password == _correct)
        {
            Confirmed = true;
            Close();
        }
        else
        {
            ErrorText.Visibility = Visibility.Visible;
            PasswordBox.Clear();
            PasswordBox.Focus();
        }
    }
}
