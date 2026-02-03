using System;
using Microsoft.Maui.Controls;
using DramaBox.Services;

namespace DramaBox.Views;

public partial class LoginView : ContentPage
{
    private readonly FirebaseAuthService _auth;
    private readonly FirebaseDatabaseService _db;
    private readonly SessionService _session;

    private bool _isPasswordVisible;

    public LoginView()
    {
        InitializeComponent();

        _auth = Resolve<FirebaseAuthService>() ?? new FirebaseAuthService(new HttpClient());
        _db = Resolve<FirebaseDatabaseService>() ?? new FirebaseDatabaseService(new HttpClient());
        _session = Resolve<SessionService>() ?? new SessionService();

        _isPasswordVisible = false;
        if (PasswordEntry != null)
            PasswordEntry.IsPassword = true;
    }

    private static T? Resolve<T>() where T : class
        => Application.Current?.Handler?.MauiContext?.Services?.GetService(typeof(T)) as T;

    private void OnTogglePasswordClicked(object sender, EventArgs e)
    {
        _isPasswordVisible = !_isPasswordVisible;

        if (PasswordEntry != null)
            PasswordEntry.IsPassword = !_isPasswordVisible;
    }

    private async void OnLoginClicked(object sender, EventArgs e)
    {
        var email = EmailEntry?.Text?.Trim() ?? "";
        var password = PasswordEntry?.Text ?? "";

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            await DisplayAlert("Login", "Preencha email e senha.", "OK");
            return;
        }

        try
        {
            var (ok, message, result) = await _auth.SignInAsync(email, password);
            if (!ok || result == null)
            {
                await DisplayAlert("Login", message, "OK");
                return;
            }

            _session.SetSession(result.IdToken, result.RefreshToken, result.LocalId, result.Email);

            var profile = await _db.GetUserProfileAsync(result.LocalId, result.IdToken);
            _session.SetProfile(profile);

            // ? Vai para a aba Discover do AppShell
            await Shell.Current.GoToAsync("//discover");
        }
        catch
        {
            await DisplayAlert("Login", "Erro inesperado ao realizar login.", "OK");
        }
    }

    private async void OnForgotPasswordClicked(object sender, EventArgs e)
    {
        var email = EmailEntry?.Text?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(email))
        {
            await DisplayAlert("Recuperar senha", "Informe seu email primeiro.", "OK");
            return;
        }

        var (ok, msg) = await _auth.SendPasswordResetAsync(email);
        await DisplayAlert("Recuperar senha", msg, "OK");
    }

    private async void OnCreateAccountClicked(object sender, EventArgs e)
    {
        // ? abre Register dentro do Shell
        await Shell.Current.GoToAsync("register");
    }
}
