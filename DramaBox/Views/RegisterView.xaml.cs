using System;
using Microsoft.Maui.Controls;
using DramaBox.Models;
using DramaBox.Services;

namespace DramaBox.Views;

public partial class RegisterView : ContentPage
{
    private readonly FirebaseAuthService _auth;
    private readonly FirebaseDatabaseService _db;
    private readonly SessionService _session;

    private bool _isPasswordVisible;

    public RegisterView()
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

    private async void OnRegisterClicked(object sender, EventArgs e)
    {
        var name = NameEntry?.Text?.Trim() ?? "";
        var email = EmailEntry?.Text?.Trim() ?? "";
        var password = PasswordEntry?.Text ?? "";

        if (string.IsNullOrWhiteSpace(name) ||
            string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(password))
        {
            await DisplayAlert("Cadastro", "Preencha nome, email e senha.", "OK");
            return;
        }

        try
        {
            var (ok, message, result) = await _auth.SignUpAsync(email, password);
            if (!ok || result == null)
            {
                await DisplayAlert("Cadastro", message, "OK");
                return;
            }

            _session.SetSession(result.IdToken, result.RefreshToken, result.LocalId, result.Email);

            var profile = new UserProfile
            {
                UserId = result.LocalId,
                Email = result.Email,
                DisplayName = name
            };

            var (saved, saveMsg) = await _db.UpsertUserProfileAsync(result.LocalId, profile, result.IdToken);
            if (!saved)
            {
                await DisplayAlert("Cadastro", saveMsg, "OK");
                return;
            }

            _session.SetProfile(profile);

            // ? Entra direto no app (Discover)
            await Shell.Current.GoToAsync("//discover");
        }
        catch
        {
            await DisplayAlert("Cadastro", "Erro inesperado ao criar conta.", "OK");
        }
    }

    private async void OnBackToLoginClicked(object sender, EventArgs e)
    {
        // volta na navegação do Shell
        await Shell.Current.GoToAsync("..");
    }
}
