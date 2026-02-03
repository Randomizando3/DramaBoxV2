// Services/FirebaseDatabaseService.cs
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DramaBox.Config;
using DramaBox.Models;

namespace DramaBox.Services;

public sealed class FirebaseDatabaseService
{
    private readonly HttpClient _http;

    public FirebaseDatabaseService(HttpClient http)
    {
        _http = http;
    }

    private static string BaseUrl => FirebaseConfig.NormalizeDbUrl(FirebaseConfig.RealtimeBaseUrl);

    private static string WithAuth(string url, string? idToken)
    {
        // Regras por idToken (recomendado). Se você usa rules abertas, pode passar null/empty.
        if (string.IsNullOrWhiteSpace(idToken))
            return url;

        var sep = url.Contains('?') ? "&" : "?";
        return $"{url}{sep}auth={Uri.EscapeDataString(idToken)}";
    }

    // Sugestão de estrutura:
    // /users/{uid}/profile
    public async Task<(bool ok, string message)> UpsertUserProfileAsync(string userId, UserProfile profile, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return (false, "UserId inválido.");

        var url = $"{BaseUrl}/users/{userId}/profile.json";
        url = WithAuth(url, idToken);

        var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var resp = await _http.PutAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));

        if (!resp.IsSuccessStatusCode)
            return (false, "Falha ao salvar perfil no Realtime Database.");

        return (true, "OK");
    }

    public async Task<UserProfile?> GetUserProfileAsync(string userId, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return null;

        var url = $"{BaseUrl}/users/{userId}/profile.json";
        url = WithAuth(url, idToken);

        try
        {
            return await _http.GetFromJsonAsync<UserProfile>(url, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch
        {
            return null;
        }
    }
}
