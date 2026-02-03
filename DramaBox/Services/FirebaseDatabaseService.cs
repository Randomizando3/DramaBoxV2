// Services/FirebaseDatabaseService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
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
        if (string.IsNullOrWhiteSpace(idToken))
            return url;

        var sep = url.Contains('?') ? "&" : "?";
        return $"{url}{sep}auth={Uri.EscapeDataString(idToken)}";
    }

    private static JsonSerializerOptions JsonOptions => new(JsonSerializerDefaults.Web);

    // ==========================================
    // GENÉRICOS (para bater com seu code-behind)
    // ==========================================

    public async Task<T?> GetAsync<T>(string path, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return default;

        var url = $"{BaseUrl}/{path.TrimStart('/')}.json";
        url = WithAuth(url, idToken);

        try
        {
            return await _http.GetFromJsonAsync<T>(url, JsonOptions);
        }
        catch
        {
            return default;
        }
    }

    public async Task<(bool ok, string message)> PutAsync(string path, object body, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return (false, "Path inválido.");

        var url = $"{BaseUrl}/{path.TrimStart('/')}.json";
        url = WithAuth(url, idToken);

        try
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            var resp = await _http.PutAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));

            if (!resp.IsSuccessStatusCode)
                return (false, $"Falha ao salvar (PUT). HTTP {(int)resp.StatusCode}.");

            return (true, "OK");
        }
        catch (Exception ex)
        {
            return (false, $"Falha ao salvar (PUT): {ex.Message}");
        }
    }

    public async Task<(bool ok, string message)> PatchAsync(string path, object body, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return (false, "Path inválido.");

        var url = $"{BaseUrl}/{path.TrimStart('/')}.json";
        url = WithAuth(url, idToken);

        try
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);

            using var req = new HttpRequestMessage(new HttpMethod("PATCH"), url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            var resp = await _http.SendAsync(req);

            if (!resp.IsSuccessStatusCode)
                return (false, $"Falha ao salvar (PATCH). HTTP {(int)resp.StatusCode}.");

            return (true, "OK");
        }
        catch (Exception ex)
        {
            return (false, $"Falha ao salvar (PATCH): {ex.Message}");
        }
    }

    // ==========================================
    // ESPECÍFICOS
    // /users/{uid}/profile
    // ==========================================

    public async Task<(bool ok, string message)> UpsertUserProfileAsync(string userId, UserProfile profile, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return (false, "UserId inválido.");

        return await PutAsync($"users/{userId}/profile", profile, idToken);
    }

    public async Task<UserProfile?> GetUserProfileAsync(string userId, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return null;

        return await GetAsync<UserProfile>($"users/{userId}/profile", idToken);
    }

    // ==========================================
    // CATALOG / DRAMAS
    // /catalog/dramas/{dramaId}
    // /catalog/dramas/{dramaId}/episodes/{episodeId}
    // ==========================================

    private static readonly JsonSerializerOptions JsonWeb = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    // ✅ ADICIONADO: método que suas Views estão chamando
    // RTDB retorna objeto: { "d001": {...}, "d002": {...} }
    public async Task<Dictionary<string, DramaSeries>> GetDramasMapAsync(string? idToken = null)
    {
        var url = $"{BaseUrl}/catalog/dramas.json";
        url = WithAuth(url, idToken);

        try
        {
            var dict = await _http.GetFromJsonAsync<Dictionary<string, DramaSeries>>(url, JsonWeb)
                       ?? new Dictionary<string, DramaSeries>();

            // garante Id preenchido pra navegação funcionar (item.Id)
            foreach (var kv in dict)
            {
                if (kv.Value != null)
                    kv.Value.Id = kv.Key;
            }

            return dict;
        }
        catch
        {
            return new Dictionary<string, DramaSeries>();
        }
    }

    public async Task<List<DramaSeries>> GetAllDramasAsync(string? idToken = null)
    {
        var url = $"{BaseUrl}/catalog/dramas.json";
        url = WithAuth(url, idToken);

        try
        {
            var dict = await _http.GetFromJsonAsync<Dictionary<string, DramaSeries>>(url, JsonWeb);
            if (dict == null) return new();

            foreach (var kv in dict)
                kv.Value.Id = kv.Key;

            return dict.Values
                .OrderByDescending(x => x.IsFeatured)
                .ThenBy(x => x.TopRank == 0 ? int.MaxValue : x.TopRank)
                .ThenByDescending(x => x.UpdatedAtUnix)
                .ToList();
        }
        catch
        {
            return new();
        }
    }

    public async Task<DramaSeries?> GetDramaAsync(string dramaId, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(dramaId)) return null;

        var url = $"{BaseUrl}/catalog/dramas/{dramaId}.json";
        url = WithAuth(url, idToken);

        try
        {
            var item = await _http.GetFromJsonAsync<DramaSeries>(url, JsonWeb);
            if (item != null) item.Id = dramaId;
            return item;
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<DramaEpisode>> GetEpisodesAsync(string dramaId, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(dramaId)) return new();

        var url = $"{BaseUrl}/catalog/dramas/{dramaId}/episodes.json";
        url = WithAuth(url, idToken);

        try
        {
            var dict = await _http.GetFromJsonAsync<Dictionary<string, DramaEpisode>>(url, JsonWeb);
            if (dict == null) return new();

            foreach (var kv in dict)
                kv.Value.Id = kv.Key;

            return dict.Values
                .OrderBy(x => x.Number)
                .ToList();
        }
        catch
        {
            return new();
        }
    }
}
