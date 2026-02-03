// Models/UserProfile.cs
namespace DramaBox.Models;

public sealed class UserProfile
{
    public string UserId { get; set; } = "";     // localId do Firebase Auth
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = ""; // Nome
    public string PhotoUrl { get; set; } = "";    // futuro
    public bool IsPremium { get; set; } = false;  // futuro
    public long CreatedAtUnix { get; set; }        // unix seconds
        = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
