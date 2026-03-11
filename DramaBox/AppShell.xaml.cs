using DramaBox.Services;
using DramaBox.Views;

namespace DramaBox;

public partial class AppShell : Shell
{
    private readonly SessionService _session;

    public AppShell()
    {
        InitializeComponent();

        _session = Resolve<SessionService>() ?? new SessionService();

        Routing.RegisterRoute("login", typeof(LoginView));
        Routing.RegisterRoute("register", typeof(RegisterView));
        Routing.RegisterRoute("upgrade", typeof(Upgrade));
        Routing.RegisterRoute("affiliates", typeof(AffiliatesView));
        Routing.RegisterRoute("admin", typeof(AdminView));

        Navigated += OnShellNavigated;
        UpdateAdminTabVisibility();
    }

    private static T? Resolve<T>() where T : class
        => Application.Current?.Handler?.MauiContext?.Services?.GetService(typeof(T)) as T;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        UpdateAdminTabVisibility();
    }

    private void OnShellNavigated(object? sender, ShellNavigatedEventArgs e)
        => UpdateAdminTabVisibility();

    private void UpdateAdminTabVisibility()
    {
        if (AdminTab == null) return;
        AdminTab.IsVisible = _session.IsAdmin;
    }
}
