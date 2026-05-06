using SimulatorApp.Shared.Services;
using System.Windows;
using System.Windows.Input;

namespace SimulatorApp.Views;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            UserNameBox.Focus();
            UserNameBox.SelectAll();
        };
    }

    private void Login_Click(object sender, RoutedEventArgs e)
    {
        TryLogin();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            TryLogin();
            e.Handled = true;
        }
    }

    private void TryLogin()
    {
        if (AuthService.Current.TryLogin(UserNameBox.Text, PasswordBox.Password))
        {
            DialogResult = true;
            return;
        }

        ErrorText.Text = "用户名或密码错误";
        PasswordBox.Clear();
        PasswordBox.Focus();
    }
}
