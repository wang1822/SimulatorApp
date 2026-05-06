using CommunityToolkit.Mvvm.ComponentModel;

namespace SimulatorApp.Shared.Services;

public enum UserRole
{
    Admin,
    User
}

public sealed partial class AuthService : ObservableObject
{
    public static AuthService Current { get; } = new();

    [ObservableProperty]
    private string _userName = string.Empty;

    [ObservableProperty]
    private UserRole _role = UserRole.User;

    public bool IsLoggedIn { get; private set; }
    public bool IsAdmin => IsLoggedIn && Role == UserRole.Admin;
    public bool IsUser => IsLoggedIn && Role == UserRole.User;

    private AuthService()
    {
    }

    public bool TryLogin(string userName, string password)
    {
        userName = (userName ?? string.Empty).Trim();
        password ??= string.Empty;

        if (userName == "admin" && password == "dy@2026..")
        {
            SetSession("admin", UserRole.Admin);
            return true;
        }

        if (userName == "user" && password == "user123")
        {
            SetSession("user", UserRole.User);
            return true;
        }

        return false;
    }

    public void Logout()
    {
        UserName = string.Empty;
        Role = UserRole.User;
        IsLoggedIn = false;
        OnPropertyChanged(nameof(IsLoggedIn));
        OnPropertyChanged(nameof(IsAdmin));
        OnPropertyChanged(nameof(IsUser));
    }

    private void SetSession(string userName, UserRole role)
    {
        UserName = userName;
        Role = role;
        IsLoggedIn = true;
        OnPropertyChanged(nameof(IsLoggedIn));
        OnPropertyChanged(nameof(IsAdmin));
        OnPropertyChanged(nameof(IsUser));
    }
}
