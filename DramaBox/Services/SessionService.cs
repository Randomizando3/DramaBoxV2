// Services/SessionService.cs
using DramaBox.Models;
using Microsoft.Maui.Storage;

namespace DramaBox.Services;

public sealed class SessionService
{
    private const string K_Email = "dramabox.auto.email";
    private const string K_Password = "dramabox.auto.password";

    public string IdToken { get; private set; } = "";
    public string RefreshToken { get; private set; } = "";
    public string UserId { get; private set; } = "";
    public string Email { get; private set; } = "";

    public UserProfile? Profile { get; private set; }

    public bool IsAuthenticated =>
        !string.IsNullOrWhiteSpace(IdToken) &&
        !string.IsNullOrWhiteSpace(UserId);

    // 🔹 Mantido igual ao seu
    public void SetSession(string idToken, string refreshToken, string userId, string email)
    {
        IdToken = idToken ?? "";
        RefreshToken = refreshToken ?? "";
        UserId = userId ?? "";
        Email = email ?? "";
    }

    // 🔹 Mantido
    public void SetProfile(UserProfile? profile)
        => Profile = profile;

    public void Clear()
    {
        IdToken = "";
        RefreshToken = "";
        UserId = "";
        Email = "";
        Profile = null;
    }

    // =====================================================
    // 🔥 NOVO — Persistência simples para relogin
    // =====================================================

    public void SaveCredentials(string email, string password)
    {
        Preferences.Set(K_Email, email ?? "");
        Preferences.Set(K_Password, password ?? "");
    }

    public (string? email, string? password) GetCredentials()
    {
        var email = Preferences.Get(K_Email, "");
        var password = Preferences.Get(K_Password, "");

        if (string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(password))
            return (null, null);

        return (email, password);
    }

    public void ClearCredentials()
    {
        Preferences.Remove(K_Email);
        Preferences.Remove(K_Password);
    }
}