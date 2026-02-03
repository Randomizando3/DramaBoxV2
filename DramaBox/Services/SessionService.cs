// Services/SessionService.cs
using DramaBox.Models;

namespace DramaBox.Services;

public sealed class SessionService
{
    public string IdToken { get; private set; } = "";
    public string RefreshToken { get; private set; } = "";
    public string UserId { get; private set; } = "";
    public string Email { get; private set; } = "";

    public UserProfile? Profile { get; private set; }

    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(IdToken) && !string.IsNullOrWhiteSpace(UserId);

    public void SetSession(string idToken, string refreshToken, string userId, string email)
    {
        IdToken = idToken ?? "";
        RefreshToken = refreshToken ?? "";
        UserId = userId ?? "";
        Email = email ?? "";
    }

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
}
