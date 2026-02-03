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
    // GENÉRICOS
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

    public async Task<(bool ok, string message)> DeleteAsync(string path, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return (false, "Path inválido.");

        var url = $"{BaseUrl}/{path.TrimStart('/')}.json";
        url = WithAuth(url, idToken);

        try
        {
            var resp = await _http.DeleteAsync(url);
            if (!resp.IsSuccessStatusCode)
                return (false, $"Falha ao remover (DELETE). HTTP {(int)resp.StatusCode}.");

            return (true, "OK");
        }
        catch (Exception ex)
        {
            return (false, $"Falha ao remover (DELETE): {ex.Message}");
        }
    }

    // ==========================================
    // PERFIL
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

    public async Task<Dictionary<string, DramaSeries>> GetDramasMapAsync(string? idToken = null)
    {
        var url = $"{BaseUrl}/catalog/dramas.json";
        url = WithAuth(url, idToken);

        try
        {
            var dict = await _http.GetFromJsonAsync<Dictionary<string, DramaSeries>>(url, JsonWeb)
                       ?? new Dictionary<string, DramaSeries>();

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

    // ==========================================
    // PLAYLIST (SALVOS)  +  CONTINUE ASSISTINDO
    // ==========================================
    // Playlist: /users/{uid}/playlist/{dramaId}
    // Continue:  /users/{uid}/continue/{dramaId}
    // ==========================================

    public sealed class PlaylistItem
    {
        public string DramaId { get; set; } = "";
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string CoverUrl { get; set; } = "";
        public long AddedAtUnix { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    public sealed class ContinueWatchingItem
    {
        public string DramaId { get; set; } = "";
        public string DramaTitle { get; set; } = "";
        public string DramaCoverUrl { get; set; } = "";

        public string EpisodeId { get; set; } = "";
        public int EpisodeNumber { get; set; }
        public string EpisodeTitle { get; set; } = "";
        public string VideoUrl { get; set; } = "";

        public double PositionSeconds { get; set; }
        public long UpdatedAtUnix { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    // ✅ AJUSTE PRINCIPAL:
    // Lê a playlist como JsonElement para suportar:
    // - formato antigo: users/{uid}/playlist/{dramaId} = true/false
    // - formato novo:   users/{uid}/playlist/{dramaId} = { PlaylistItem }
    public async Task<Dictionary<string, PlaylistItem>> GetPlaylistMapAsync(string userId, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return new();

        try
        {
            var raw = await GetAsync<Dictionary<string, JsonElement>>($"users/{userId}/playlist", idToken);
            if (raw == null || raw.Count == 0)
                return new Dictionary<string, PlaylistItem>();

            var map = new Dictionary<string, PlaylistItem>();

            foreach (var kv in raw)
            {
                var key = kv.Key;
                var el = kv.Value;

                // formato antigo: true/false
                if (el.ValueKind == JsonValueKind.True || el.ValueKind == JsonValueKind.False)
                {
                    if (el.ValueKind == JsonValueKind.True)
                    {
                        map[key] = new PlaylistItem
                        {
                            DramaId = key,
                            Title = "(Salvo)",
                            Subtitle = "",
                            CoverUrl = "",
                            AddedAtUnix = 0
                        };
                    }
                    continue;
                }

                // formato novo: objeto
                if (el.ValueKind == JsonValueKind.Object)
                {
                    try
                    {
                        var item = JsonSerializer.Deserialize<PlaylistItem>(el.GetRawText(), JsonWeb);
                        if (item != null)
                        {
                            if (string.IsNullOrWhiteSpace(item.DramaId))
                                item.DramaId = key;
                            map[key] = item;
                        }
                    }
                    catch
                    {
                        // ignora item quebrado sem matar a lista inteira
                    }
                }
            }

            return map;
        }
        catch
        {
            return new Dictionary<string, PlaylistItem>();
        }
    }

    public async Task<List<PlaylistItem>> GetPlaylistAsync(string userId, string? idToken = null)
    {
        var map = await GetPlaylistMapAsync(userId, idToken);

        foreach (var kv in map)
        {
            if (kv.Value != null && string.IsNullOrWhiteSpace(kv.Value.DramaId))
                kv.Value.DramaId = kv.Key;
        }

        return map.Values
            .Where(x => x != null)
            .OrderByDescending(x => x.AddedAtUnix)
            .ToList();
    }

    // ✅ Também precisa ser tolerante ao formato antigo bool
    public async Task<bool> IsInPlaylistAsync(string userId, string dramaId, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(dramaId))
            return false;

        try
        {
            var el = await GetAsync<JsonElement?>($"users/{userId}/playlist/{dramaId}", idToken);
            if (el == null)
                return false;

            var v = el.Value;

            if (v.ValueKind == JsonValueKind.True) return true;
            if (v.ValueKind == JsonValueKind.False) return false;
            if (v.ValueKind == JsonValueKind.Object) return true;

            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<(bool ok, string message)> AddToPlaylistAsync(string userId, DramaSeries drama, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return (false, "UserId inválido.");

        if (drama == null || string.IsNullOrWhiteSpace(drama.Id))
            return (false, "Drama inválido (sem Id).");

        // ✅ CoverUrl pode vir vazio; usa PosterUrl como fallback
        var cover = drama.CoverUrl;
        if (string.IsNullOrWhiteSpace(cover))
            cover = drama.PosterUrl;

        var item = new PlaylistItem
        {
            DramaId = drama.Id ?? "",
            Title = drama.Title ?? "",
            Subtitle = drama.Subtitle ?? "",
            CoverUrl = cover ?? "",
            AddedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        return await PutAsync($"users/{userId}/playlist/{drama.Id}", item, idToken);
    }

    public async Task<(bool ok, string message)> RemoveFromPlaylistAsync(string userId, string dramaId, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(dramaId))
            return (false, "Dados inválidos.");

        return await DeleteAsync($"users/{userId}/playlist/{dramaId}", idToken);
    }

    public async Task<(bool ok, string message, bool nowSaved)> TogglePlaylistAsync(string userId, DramaSeries drama, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return (false, "UserId inválido.", false);

        if (drama == null || string.IsNullOrWhiteSpace(drama.Id))
            return (false, "Drama inválido (sem Id).", false);

        var exists = await IsInPlaylistAsync(userId, drama.Id!, idToken);
        if (exists)
        {
            var r = await RemoveFromPlaylistAsync(userId, drama.Id!, idToken);
            return (r.ok, r.message, false);
        }
        else
        {
            var a = await AddToPlaylistAsync(userId, drama, idToken);
            return (a.ok, a.message, true);
        }
    }

    public async Task<Dictionary<string, ContinueWatchingItem>> GetContinueMapAsync(string userId, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return new();

        return await GetAsync<Dictionary<string, ContinueWatchingItem>>($"users/{userId}/continue", idToken)
               ?? new Dictionary<string, ContinueWatchingItem>();
    }

    public async Task<List<ContinueWatchingItem>> GetContinueAsync(string userId, string? idToken = null, int take = 30)
    {
        var map = await GetContinueMapAsync(userId, idToken);

        foreach (var kv in map)
        {
            if (kv.Value != null && string.IsNullOrWhiteSpace(kv.Value.DramaId))
                kv.Value.DramaId = kv.Key;
        }

        return map.Values
            .Where(x => x != null)
            .OrderByDescending(x => x.UpdatedAtUnix)
            .Take(take)
            .ToList();
    }

    public async Task<(bool ok, string message)> UpsertContinueAsync(string userId, ContinueWatchingItem item, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return (false, "UserId inválido.");

        if (item == null || string.IsNullOrWhiteSpace(item.DramaId))
            return (false, "Item inválido.");

        item.UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return await PutAsync($"users/{userId}/continue/{item.DramaId}", item, idToken);
    }

    public async Task<(bool ok, string message)> RemoveContinueAsync(string userId, string dramaId, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(dramaId))
            return (false, "Dados inválidos.");

        return await DeleteAsync($"users/{userId}/continue/{dramaId}", idToken);
    }

    // Wrapper para compatibilidade com o nome que seu PlayerPage está chamando
    // ==========================================
    // CONTINUE ASSISTINDO (método compatível com PlayerPage)
    // /users/{uid}/continue/{dramaId}
    // ==========================================
    public async Task<(bool ok, string message)> UpsertContinueWatchingAsync(
        string userId,
        string dramaId,
        string dramaTitle,
        string coverUrl,
        string episodeId,
        int episodeNumber,
        string episodeTitle,
        string videoUrl,
        long positionSeconds,
        string? idToken = null
    )
    {
        if (string.IsNullOrWhiteSpace(userId))
            return (false, "UserId inválido.");

        if (string.IsNullOrWhiteSpace(dramaId))
            return (false, "DramaId inválido.");

        var item = new ContinueWatchingItem
        {
            DramaId = dramaId ?? "",
            DramaTitle = dramaTitle ?? "",
            DramaCoverUrl = coverUrl ?? "",

            EpisodeId = episodeId ?? "",
            EpisodeNumber = episodeNumber,
            EpisodeTitle = episodeTitle ?? "",
            VideoUrl = videoUrl ?? "",

            PositionSeconds = Math.Max(0, positionSeconds),
            UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        return await PutAsync($"users/{userId}/continue/{dramaId}", item, idToken);
    }
}
