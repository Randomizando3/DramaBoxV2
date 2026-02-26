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

    private static readonly JsonSerializerOptions JsonWeb = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

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
        // ✅ PATCH no root é válido quando queremos multi-path updates
        var clean = (path ?? "").Trim('/');

        var url = string.IsNullOrEmpty(clean)
            ? $"{BaseUrl}/.json"
            : $"{BaseUrl}/{clean}.json";

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
    // ==========================================

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
    // PLAYLIST (SALVOS) + CONTINUE ASSISTINDO
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

    public async Task<Dictionary<string, PlaylistItem>> GetPlaylistMapAsync(string userId, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return new();

        return await GetAsync<Dictionary<string, PlaylistItem>>($"users/{userId}/playlist", idToken)
               ?? new Dictionary<string, PlaylistItem>();
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

    public async Task<bool> IsInPlaylistAsync(string userId, string dramaId, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(dramaId))
            return false;

        var item = await GetAsync<PlaylistItem>($"users/{userId}/playlist/{dramaId}", idToken);
        return item != null;
    }

    public async Task<(bool ok, string message)> AddToPlaylistAsync(string userId, DramaSeries drama, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return (false, "UserId inválido.");

        if (drama == null || string.IsNullOrWhiteSpace(drama.Id))
            return (false, "Drama inválido (sem Id).");

        var item = new PlaylistItem
        {
            DramaId = drama.Id ?? "",
            Title = drama.Title ?? "",
            Subtitle = drama.Subtitle ?? "",
            CoverUrl = drama.CoverUrl ?? "",
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

    // Compatível com PlayerPage
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

    // ============================================================
    // COMMUNITY + CRIADOR + ROYALTIES
    // ============================================================

    // ---------- Models (você já tem no projeto; mantive igual ao seu fluxo) ----------
    public async Task<CommunityRoyaltyConfig> GetCommunityRoyaltiesAsync(string? idToken = null)
    {
        var cfg = await GetAsync<CommunityRoyaltyConfig>("admin/communityroyalties", idToken);
        return cfg ?? new CommunityRoyaltyConfig { UpdatedAtUnix = NowUnix() };
    }

    public async Task<(bool ok, string message)> UpsertCommunityRoyaltiesAsync(CommunityRoyaltyConfig cfg, string? idToken = null)
    {
        if (cfg == null) return (false, "Config inválida.");
        cfg.UpdatedAtUnix = NowUnix();
        return await PutAsync("admin/communityroyalties", cfg, idToken);
    }

    public async Task<(bool ok, string message)> UpsertCommunitySeriesAsync(string creatorUid, CommunitySeries series, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(creatorUid))
            return (false, "creatorUid inválido.");

        if (series == null)
            return (false, "Série inválida.");

        if (string.IsNullOrWhiteSpace(series.Id))
            series.Id = Guid.NewGuid().ToString("N");

        if (series.CreatedAtUnix <= 0)
            series.CreatedAtUnix = NowUnix();

        series.UpdatedAtUnix = NowUnix();
        series.CreatorUserId = creatorUid;

        var patch = new Dictionary<string, object?>
        {
            [$"community/series/{series.Id}"] = series,
            [$"users/{creatorUid}/createdSeries/{series.Id}"] = true
        };

        return await PatchAsync("", patch, idToken);
    }

    public async Task<(bool ok, string message)> UpsertCommunityEpisodeAsync(
        string creatorUid,
        string seriesId,
        CommunityEpisode ep,
        string? idToken = null
    )
    {
        if (string.IsNullOrWhiteSpace(creatorUid))
            return (false, "creatorUid inválido.");

        if (string.IsNullOrWhiteSpace(seriesId))
            return (false, "seriesId inválido.");

        if (ep == null)
            return (false, "Episódio inválido.");

        if (string.IsNullOrWhiteSpace(ep.Id))
            ep.Id = Guid.NewGuid().ToString("N");

        if (ep.CreatedAtUnix <= 0)
            ep.CreatedAtUnix = NowUnix();

        var (ok, msg) = await PutAsync($"community/series/{seriesId}/episodes/{ep.Id}", ep, idToken);
        if (!ok) return (ok, msg);

        await PatchAsync($"community/series/{seriesId}", new { updatedAtUnix = NowUnix() }, idToken);
        return (true, "OK");
    }

    public async Task<CommunitySeries?> GetCommunitySeriesAsync(string seriesId, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(seriesId)) return null;
        var s = await GetAsync<CommunitySeries>($"community/series/{seriesId}", idToken);
        if (s != null && string.IsNullOrWhiteSpace(s.Id)) s.Id = seriesId;
        return s;
    }

    public async Task<List<CommunityEpisode>> GetCommunityEpisodesAsync(string seriesId, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(seriesId)) return new();

        var dict = await GetAsync<Dictionary<string, CommunityEpisode>>($"community/series/{seriesId}/episodes", idToken);
        if (dict == null) return new();

        foreach (var kv in dict)
        {
            if (kv.Value != null && string.IsNullOrWhiteSpace(kv.Value.Id))
                kv.Value.Id = kv.Key;
        }

        return dict.Values
            .Where(x => x != null)
            .OrderBy(x => x.Number)
            .ToList();
    }

    public async Task<List<CommunitySeries>> GetCreatorCommunitySeriesAsync(string creatorUid, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(creatorUid)) return new();

        var map = await GetAsync<Dictionary<string, bool>>($"users/{creatorUid}/createdSeries", idToken)
                  ?? new Dictionary<string, bool>();

        var ids = map.Where(kv => kv.Value).Select(kv => kv.Key).ToList();
        if (ids.Count == 0) return new();

        var list = new List<CommunitySeries>();
        foreach (var id in ids)
        {
            var s = await GetCommunitySeriesAsync(id, idToken);
            if (s != null) list.Add(s);
        }

        return list
            .OrderByDescending(x => x.UpdatedAtUnix)
            .ToList();
    }

    public async Task<List<CommunitySeries>> GetCommunityFeedAsync(string mode, int take = 30, string? idToken = null)
    {
        mode = (mode ?? "").Trim().ToLowerInvariant();
        if (take <= 0) take = 30;

        var dict = await GetAsync<Dictionary<string, CommunitySeries>>("community/series", idToken);
        if (dict == null) return new();

        foreach (var kv in dict)
        {
            if (kv.Value != null && string.IsNullOrWhiteSpace(kv.Value.Id))
                kv.Value.Id = kv.Key;
        }

        var all = dict.Values
            .Where(x => x != null && x.IsPublished)
            .ToList();

        if (all.Count == 0) return new();

        if (mode == "random" || mode == "aleatorio" || mode == "aleatórios")
        {
            var rng = new Random();
            return all.OrderBy(_ => rng.Next()).Take(take).ToList();
        }

        var metrics = await GetAsync<Dictionary<string, CommunitySeriesMetrics>>("community/metrics/series", idToken)
                      ?? new Dictionary<string, CommunitySeriesMetrics>();

        double Score(CommunitySeries s)
        {
            metrics.TryGetValue(s.Id, out var m);
            m ??= new CommunitySeriesMetrics();

            if (mode == "recommended" || mode == "recomendados")
            {
                return (m.MinutesWatched * 2.0) + (m.Likes * 1.0) + (m.Shares * 1.5) + (s.UpdatedAtUnix / 1_000_000.0);
            }
            else
            {
                return (m.MinutesWatched * 1.5) + (m.Likes * 1.0) + (m.Shares * 2.0);
            }
        }

        return all
            .OrderByDescending(Score)
            .Take(take)
            .ToList();
    }

    private async Task<CommunitySeriesMetrics> GetSeriesMetricsAsync(string seriesId, string? idToken)
    {
        var m = await GetAsync<CommunitySeriesMetrics>($"community/metrics/series/{seriesId}", idToken);
        return m ?? new CommunitySeriesMetrics { UpdatedAtUnix = NowUnix() };
    }

    private async Task SaveSeriesMetricsAsync(string seriesId, CommunitySeriesMetrics m, string? idToken)
    {
        m.UpdatedAtUnix = NowUnix();
        await PutAsync($"community/metrics/series/{seriesId}", m, idToken);
    }

    private async Task AddEarningsAsync(string creatorUid, string seriesId, double centsDelta, string? idToken)
    {
        if (string.IsNullOrWhiteSpace(creatorUid) || string.IsNullOrWhiteSpace(seriesId))
            return;

        var totalPath = $"community/earnings/creators/{creatorUid}/centsTotal";
        var seriesPath = $"community/earnings/creators/{creatorUid}/series/{seriesId}/centsTotal";
        var updatedPath = $"community/earnings/creators/{creatorUid}/updatedAtUnix";
        var updatedSeriesPath = $"community/earnings/creators/{creatorUid}/series/{seriesId}/updatedAtUnix";

        var curTotal = await GetAsync<double?>(totalPath, idToken) ?? 0.0;
        var curSeries = await GetAsync<double?>(seriesPath, idToken) ?? 0.0;

        curTotal += centsDelta;
        curSeries += centsDelta;

        if (curTotal < 0) curTotal = 0;
        if (curSeries < 0) curSeries = 0;

        var patch = new Dictionary<string, object?>
        {
            [totalPath] = curTotal,
            [seriesPath] = curSeries,
            [updatedPath] = NowUnix(),
            [updatedSeriesPath] = NowUnix()
        };

        await PatchAsync("", patch, idToken);
    }

    public async Task<(bool ok, string message, bool nowLiked)> ToggleCommunityLikeAsync(
        string userId,
        string seriesId,
        string creatorUid,
        string? idToken = null
    )
    {
        if (string.IsNullOrWhiteSpace(userId))
            return (false, "UserId inválido.", false);

        if (string.IsNullOrWhiteSpace(seriesId))
            return (false, "SeriesId inválido.", false);

        var likePath = $"community/interactions/likes/{userId}/{seriesId}";

        var already = await GetAsync<bool?>(likePath, idToken) == true;
        var cfg = await GetCommunityRoyaltiesAsync(idToken);

        var m = await GetSeriesMetricsAsync(seriesId, idToken);

        if (already)
        {
            var del = await DeleteAsync(likePath, idToken);
            if (!del.ok) return (false, del.message, true);

            m.Likes = Math.Max(0, m.Likes - 1);
            await SaveSeriesMetricsAsync(seriesId, m, idToken);

            await AddEarningsAsync(creatorUid, seriesId, -cfg.PerLike, idToken);

            return (true, "OK", false);
        }
        else
        {
            var put = await PutAsync(likePath, true, idToken);
            if (!put.ok) return (false, put.message, false);

            m.Likes += 1;
            await SaveSeriesMetricsAsync(seriesId, m, idToken);

            await AddEarningsAsync(creatorUid, seriesId, cfg.PerLike, idToken);

            return (true, "OK", true);
        }
    }

    public async Task<(bool ok, string message)> AddCommunityShareAsync(
        string seriesId,
        string creatorUid,
        string? idToken = null
    )
    {
        if (string.IsNullOrWhiteSpace(seriesId))
            return (false, "SeriesId inválido.");

        var cfg = await GetCommunityRoyaltiesAsync(idToken);
        var m = await GetSeriesMetricsAsync(seriesId, idToken);

        m.Shares += 1;
        await SaveSeriesMetricsAsync(seriesId, m, idToken);

        await AddEarningsAsync(creatorUid, seriesId, cfg.PerShare, idToken);

        return (true, "OK");
    }

    public async Task<(bool ok, string message)> UpsertCommunityWatchSecondsAsync(
        string userId,
        string seriesId,
        string episodeId,
        string creatorUid,
        long totalSeconds,
        string? idToken = null
    )
    {
        if (string.IsNullOrWhiteSpace(userId))
            return (false, "UserId inválido.");

        if (string.IsNullOrWhiteSpace(seriesId))
            return (false, "SeriesId inválido.");

        if (string.IsNullOrWhiteSpace(episodeId))
            return (false, "EpisodeId inválido.");

        if (totalSeconds < 0) totalSeconds = 0;

        var path = $"community/interactions/watchSeconds/{userId}/{seriesId}/{episodeId}";
        var last = await GetAsync<long?>(path, idToken) ?? 0;

        if (totalSeconds <= last)
            return (true, "OK");

        var deltaSeconds = totalSeconds - last;

        var put = await PutAsync(path, totalSeconds, idToken);
        if (!put.ok) return (false, put.message);

        var deltaMinutes = deltaSeconds / 60.0;

        var m = await GetSeriesMetricsAsync(seriesId, idToken);
        m.MinutesWatched += deltaMinutes;
        await SaveSeriesMetricsAsync(seriesId, m, idToken);

        var cfg = await GetCommunityRoyaltiesAsync(idToken);
        var centsDelta = deltaMinutes * cfg.PerMinuteWatched;

        await AddEarningsAsync(creatorUid, seriesId, centsDelta, idToken);

        return (true, "OK");
    }

    // ============================================================
    // REWARDS / COINS / MISSIONS / SHOP + PREMIUM (VIP)
    // ============================================================

    // -------- Premium helpers (VIP = Premium) --------

    public async Task<long> GetPremiumUntilUnixAsync(string uid, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(uid)) return 0;

        var v = await GetAsync<long?>($"users/{uid}/profile/premiumUntilUnix", idToken);
        if (v.HasValue) return v.Value;

        return await GetAsync<long?>($"users/{uid}/profile/PremiumUntilUnix", idToken) ?? 0;
    }

    public async Task<string> GetUserPlanAsync(string uid, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(uid)) return "free";

        var plan = await GetAsync<string>($"users/{uid}/profile/plano", idToken);

        if (string.IsNullOrWhiteSpace(plan))
            plan = await GetAsync<string>($"users/{uid}/profile/Plano", idToken);

        plan = (plan ?? "").Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(plan) ? "free" : plan;
    }

    public async Task<(bool ok, string message)> SetUserPlanAndPremiumUntilAsync(string uid, string plan, long premiumUntilUnix, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(uid)) return (false, "Uid inválido.");

        plan = (plan ?? "").Trim().ToLowerInvariant();
        if (plan != "free" && plan != "premium") plan = "free";

        if (premiumUntilUnix < 0) premiumUntilUnix = 0;

        var patch = new Dictionary<string, object?>
        {
            [$"users/{uid}/profile/plano"] = plan,
            [$"users/{uid}/profile/premiumUntilUnix"] = premiumUntilUnix,

            [$"users/{uid}/profile/Plano"] = null,
            [$"users/{uid}/profile/PremiumUntilUnix"] = null
        };

        return await PatchAsync("", patch, idToken);
    }

    public async Task EnsurePremiumConsistencyAsync(string uid, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(uid)) return;

        var plan = await GetUserPlanAsync(uid, idToken);
        var until = await GetPremiumUntilUnixAsync(uid, idToken);
        var now = NowUnix();

        // premium sem prazo = until==0 (mantém)
        if (plan == "premium" && until == 0)
            return;

        // expirou
        if (plan == "premium" && until > 0 && until <= now)
        {
            await SetUserPlanAndPremiumUntilAsync(uid, "free", 0, idToken);
            return;
        }

        // até futuro mas plano free -> corrige
        if (plan == "free" && until > now)
        {
            await SetUserPlanAndPremiumUntilAsync(uid, "premium", until, idToken);
        }
    }

    public Task EnsureVipPlanConsistencyAsync(string uid, string? idToken = null)
        => EnsurePremiumConsistencyAsync(uid, idToken);

    public async Task<string> GetVipStatusTextAsync(string uid, string? idToken = null)
    {
        await EnsurePremiumConsistencyAsync(uid, idToken);

        var plan = await GetUserPlanAsync(uid, idToken);
        var until = await GetPremiumUntilUnixAsync(uid, idToken);
        var now = NowUnix();

        if (plan != "premium")
            return "Sem VIP";

        if (until <= 0)
            return "Premium ativo";

        if (until <= now)
            return "Sem VIP";

        var dt = DateTimeOffset.FromUnixTimeSeconds(until).ToLocalTime().DateTime;
        return $"Premium • até {dt:dd/MM/yyyy}";
    }

    public async Task<(bool ok, string message)> ExtendPremiumDaysAsync(string uid, int days, string reason, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(uid)) return (false, "Uid inválido.");
        if (days <= 0) return (false, "Dias inválidos.");

        var now = NowUnix();
        var plan = await GetUserPlanAsync(uid, idToken);
        var until = await GetPremiumUntilUnixAsync(uid, idToken);

        if (plan == "premium" && until == 0)
            return (false, "Você já possui Premium ativo (sem prazo).");

        var baseStart = until > now ? until : now;
        var next = baseStart + (long)days * 24L * 60L * 60L;

        var patch = new Dictionary<string, object?>
        {
            [$"users/{uid}/profile/plano"] = "premium",
            [$"users/{uid}/profile/premiumUntilUnix"] = next,

            [$"users/{uid}/profile/Plano"] = null,
            [$"users/{uid}/profile/PremiumUntilUnix"] = null,

            [$"users/{uid}/profile/premiumLastReason"] = reason ?? "",
            [$"users/{uid}/profile/premiumUpdatedAtUnix"] = now
        };

        return await PatchAsync("", patch, idToken);
    }

    // ---------- Rewards types ----------

    public sealed class RewardDefinition
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public int Coins { get; set; }
        public bool IsDaily { get; set; }
        public bool RequiresManualApprove { get; set; }
        public string? InputLabel { get; set; }
        public string? InputPlaceholder { get; set; }
        public string Icon { get; set; } = "⭐";
        public int Sort { get; set; } = 100;
    }

    public sealed class RewardShopItem
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public int CostCoins { get; set; }
        public int VipDays { get; set; }
        public int Sort { get; set; } = 100;
    }

    public sealed class RewardClaim
    {
        public bool Done { get; set; }
        public long ClaimedAtUnix { get; set; }
        public int CoinsAwarded { get; set; }
    }

    public sealed class RewardPendingManual
    {
        public string MissionId { get; set; } = "";
        public string Uid { get; set; } = "";
        public string InputValue { get; set; } = "";
        public string Status { get; set; } = "pending"; // pending|approved|rejected
        public long CreatedAtUnix { get; set; }
        public long DecidedAtUnix { get; set; }
        public string? DecidedBy { get; set; }
    }

    public sealed class RewardCheckin
    {
        public int Streak { get; set; } = 0;
        public string LastDateKey { get; set; } = ""; // yyyyMMdd

        // ✅ este campo é só informativo (último prêmio em coins no check-in)
        public int RewardCoins { get; set; } = 5;

        public int LastCycleDay { get; set; } = 0; // 1..7
        public int LastVipDays { get; set; } = 0;  // 0 ou 1
        public long UpdatedAtUnix { get; set; }
    }

    // ---------- Catalog ----------

    public async Task<List<RewardDefinition>> GetRewardDefinitionsAsync(string? idToken = null)
    {
        var dict = await GetAsync<Dictionary<string, RewardDefinition>>("rewards/definitions", idToken)
                   ?? new Dictionary<string, RewardDefinition>();

        foreach (var kv in dict)
        {
            if (kv.Value != null && string.IsNullOrWhiteSpace(kv.Value.Id))
                kv.Value.Id = kv.Key;
        }

        return dict.Values.Where(x => x != null).ToList()!;
    }

    public async Task<List<RewardShopItem>> GetRewardShopAsync(string? idToken = null)
    {
        var dict = await GetAsync<Dictionary<string, RewardShopItem>>("rewards/shop", idToken)
                   ?? new Dictionary<string, RewardShopItem>();

        foreach (var kv in dict)
        {
            if (kv.Value != null && string.IsNullOrWhiteSpace(kv.Value.Id))
                kv.Value.Id = kv.Key;
        }

        return dict.Values.Where(x => x != null).ToList()!;
    }

    public async Task EnsureRewardsCatalogDefaultsAsync(string? idToken = null)
    {
        var defs = await GetAsync<Dictionary<string, RewardDefinition>>("rewards/definitions", idToken);
        var shop = await GetAsync<Dictionary<string, RewardShopItem>>("rewards/shop", idToken);

        var needDefs = defs == null || defs.Count == 0;
        var needShop = shop == null || shop.Count == 0;

        if (!needDefs && !needShop) return;

        var patch = new Dictionary<string, object?>();

        if (needDefs)
        {
            var defaults = new List<RewardDefinition>
            {
                new() { Id="daily_watch_1", Title="Assistir 1 drama", Description="Assista qualquer episódio até o fim.", Coins=10, IsDaily=true, Icon="🎬", Sort=10 },
                new() { Id="daily_like_1", Title="Curtir 1 drama", Description="Curta uma série/episódio.", Coins=6, IsDaily=true, Icon="❤️", Sort=20 },
                new() { Id="daily_share_1", Title="Compartilhar 1 drama", Description="Compartilhe um episódio com alguém.", Coins=8, IsDaily=true, Icon="📤", Sort=30 },
                new() { Id="daily_watch_3", Title="Assistir 3 dramas", Description="Complete 3 episódios hoje.", Coins=20, IsDaily=true, Icon="🔥", Sort=40 },
                new() { Id="daily_like_3", Title="Curtir 3 dramas", Description="Curta 3 episódios hoje.", Coins=15, IsDaily=true, Icon="💖", Sort=50 },
                new() { Id="daily_share_3", Title="Compartilhar 3 dramas", Description="Compartilhe 3 episódios hoje.", Coins=18, IsDaily=true, Icon="🚀", Sort=60 },


                new()
                {
                    Id="perm_follow_official",
                    Title="Curtir/seguir página oficial",
                    Description="Envie seu @/link. Eu confirmo e libero a recompensa.",
                    Coins=40,
                    IsDaily=false,
                    RequiresManualApprove=true,
                    InputLabel="Cole seu usuário ou link (ex.: @dany / instagram.com/...)",
                    InputPlaceholder="@seu_usuario",
                    Icon="⭐",
                    Sort=130
                }
            };

            foreach (var d in defaults)
                patch[$"rewards/definitions/{d.Id}"] = d;
        }

        if (needShop)
        {
            var items = new List<RewardShopItem>
            {
                new() { Id="vip_1d", Title="VIP 1 dia", Description="Desbloqueia conteúdos VIP por 24h.", CostCoins=120, VipDays=1, Sort=10 },
                new() { Id="vip_3d", Title="VIP 3 dias", Description="Desbloqueia conteúdos VIP por 3 dias.", CostCoins=300, VipDays=3, Sort=20 },
                new() { Id="vip_7d", Title="VIP 7 dias", Description="Desbloqueia conteúdos VIP por 7 dias.", CostCoins=600, VipDays=7, Sort=30 }
            };

            foreach (var it in items)
                patch[$"rewards/shop/{it.Id}"] = it;
        }

        if (patch.Count > 0)
            await PatchAsync("", patch, idToken);
    }

    // ---------- Wallet ----------

    public async Task<int> GetUserCoinsAsync(string uid, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(uid)) return 0;

        var v = await GetAsync<int?>($"users/{uid}/wallet/coins", idToken);
        if (v.HasValue && v.Value >= 0) return v.Value;
        return 0;
    }

    public async Task<(bool ok, string message)> AddUserCoinsAsync(string uid, int delta, string reason, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(uid)) return (false, "Uid inválido.");

        var cur = await GetUserCoinsAsync(uid, idToken);
        var next = cur + delta;
        if (next < 0) return (false, "Coins insuficientes.");

        var patch = new Dictionary<string, object?>
        {
            [$"users/{uid}/wallet/coins"] = next,
            [$"users/{uid}/wallet/updatedAtUnix"] = NowUnix(),
            [$"users/{uid}/wallet/lastReason"] = reason ?? ""
        };

        return await PatchAsync("", patch, idToken);
    }

    // ---------- Missions state ----------

    public async Task<Dictionary<string, RewardClaim>> GetUserDailyMissionsStateAsync(string uid, string dateKey, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(uid) || string.IsNullOrWhiteSpace(dateKey))
            return new();

        return await GetAsync<Dictionary<string, RewardClaim>>($"users/{uid}/rewards/daily/{dateKey}", idToken)
               ?? new Dictionary<string, RewardClaim>();
    }

    public async Task<Dictionary<string, RewardClaim>> GetUserPermanentMissionsStateAsync(string uid, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(uid))
            return new();

        return await GetAsync<Dictionary<string, RewardClaim>>($"users/{uid}/rewards/permanent", idToken)
               ?? new Dictionary<string, RewardClaim>();
    }

    public async Task<(bool ok, string message)> CompleteMissionAndAwardAsync(string uid, string missionId, string todayKey, bool isDaily, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(uid)) return (false, "Uid inválido.");
        if (string.IsNullOrWhiteSpace(missionId)) return (false, "Missão inválida.");

        var path = isDaily
            ? $"users/{uid}/rewards/daily/{todayKey}/{missionId}"
            : $"users/{uid}/rewards/permanent/{missionId}";

        var already = await GetAsync<RewardClaim>(path, idToken);
        if (already?.Done == true)
            return (false, "Missão já concluída.");

        var def = await GetAsync<RewardDefinition>($"rewards/definitions/{missionId}", idToken);
        if (def == null) return (false, "Missão não encontrada no catálogo.");

        if (def.RequiresManualApprove)
            return (false, "Essa missão precisa de aprovação manual.");

        var now = NowUnix();

        var add = await AddUserCoinsAsync(uid, def.Coins, $"mission:{missionId}", idToken);
        if (!add.ok) return add;

        var claim = new RewardClaim
        {
            Done = true,
            ClaimedAtUnix = now,
            CoinsAwarded = def.Coins
        };

        var patch = new Dictionary<string, object?>
        {
            [path] = claim
        };

        return await PatchAsync("", patch, idToken);
    }

    // ---------- Manual approval flow ----------

    public async Task<(bool ok, string message)> SubmitManualMissionForApprovalAsync(string uid, string missionId, string inputValue, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(uid)) return (false, "Uid inválido.");
        if (string.IsNullOrWhiteSpace(missionId)) return (false, "Missão inválida.");
        if (string.IsNullOrWhiteSpace(inputValue)) return (false, "Informe o usuário/link.");

        var def = await GetAsync<RewardDefinition>($"rewards/definitions/{missionId}", idToken);
        if (def == null) return (false, "Missão não encontrada.");
        if (!def.RequiresManualApprove) return (false, "Essa missão não é manual.");

        var perm = await GetAsync<RewardClaim>($"users/{uid}/rewards/permanent/{missionId}", idToken);
        if (perm?.Done == true) return (false, "Missão já concluída.");

        var now = NowUnix();
        var pendingPath = $"admin/rewards/pending/{uid}/{missionId}";

        var current = await GetAsync<RewardPendingManual>(pendingPath, idToken);
        if (current != null && string.Equals(current.Status, "pending", StringComparison.OrdinalIgnoreCase))
            return (false, "Já enviado. Está em análise.");

        var obj = new RewardPendingManual
        {
            MissionId = missionId,
            Uid = uid,
            InputValue = inputValue.Trim(),
            Status = "pending",
            CreatedAtUnix = now
        };

        return await PutAsync(pendingPath, obj, idToken);
    }

    public async Task<Dictionary<string, RewardPendingManual>> GetUserPendingManualMissionsAsync(string uid, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(uid)) return new();

        var dict = await GetAsync<Dictionary<string, RewardPendingManual>>($"admin/rewards/pending/{uid}", idToken)
                   ?? new Dictionary<string, RewardPendingManual>();

        foreach (var kv in dict)
        {
            if (kv.Value != null && string.IsNullOrWhiteSpace(kv.Value.MissionId))
                kv.Value.MissionId = kv.Key;
        }

        return dict;
    }

    public async Task<(bool ok, string message)> SyncApprovedManualMissionsAsync(string uid, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(uid)) return (false, "Uid inválido.");

        var pend = await GetUserPendingManualMissionsAsync(uid, idToken);
        if (pend.Count == 0) return (true, "OK");

        foreach (var kv in pend)
        {
            var p = kv.Value;
            if (p == null) continue;

            if (!string.Equals(p.Status, "approved", StringComparison.OrdinalIgnoreCase))
                continue;

            var missionId = p.MissionId;

            var claimPath = $"users/{uid}/rewards/permanent/{missionId}";
            var already = await GetAsync<RewardClaim>(claimPath, idToken);
            if (already?.Done == true)
            {
                await DeleteAsync($"admin/rewards/pending/{uid}/{missionId}", idToken);
                continue;
            }

            var def = await GetAsync<RewardDefinition>($"rewards/definitions/{missionId}", idToken);
            if (def == null)
            {
                await DeleteAsync($"admin/rewards/pending/{uid}/{missionId}", idToken);
                continue;
            }

            var add = await AddUserCoinsAsync(uid, def.Coins, $"manual_mission:{missionId}", idToken);
            if (!add.ok) continue;

            var now = NowUnix();
            var claim = new RewardClaim { Done = true, ClaimedAtUnix = now, CoinsAwarded = def.Coins };

            var patch = new Dictionary<string, object?>
            {
                [claimPath] = claim
            };

            await PatchAsync("", patch, idToken);
            await DeleteAsync($"admin/rewards/pending/{uid}/{missionId}", idToken);
        }

        return (true, "OK");
    }

    // ---------- Check-in ----------

    public async Task<RewardCheckin> GetUserCheckinAsync(string uid, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(uid)) return new RewardCheckin();

        var c = await GetAsync<RewardCheckin>($"users/{uid}/rewards/checkin", idToken);
        return c ?? new RewardCheckin();
    }

    /// <summary>
    /// ✅ Auto-check-in idempotente: se já fez hoje, não faz nada.
    /// Chame isso no início do ReloadAsync (antes de ler coins / UI).
    /// </summary>
    public async Task<(bool ok, bool applied, string message)> EnsureAutoDailyCheckinAsync(string uid, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(uid))
            return (false, false, "Uid inválido.");

        var today = DateTime.Now.ToString("yyyyMMdd");
        var c = await GetUserCheckinAsync(uid, idToken);

        if (string.Equals(c.LastDateKey, today, StringComparison.OrdinalIgnoreCase))
            return (true, false, "Já aplicado hoje.");

        var r = await TryDailyCheckinAsync(uid, idToken);
        return (r.ok, r.ok, r.message);
    }

    public async Task<(bool ok, string message)> TryDailyCheckinAsync(string uid, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(uid)) return (false, "Uid inválido.");

        // regra: 1–6 => +5 coins; 7 => +1 dia VIP; repete
        const int coinsPerDay = 5;
        const int cycleLen = 7;

        var today = DateTime.Now.ToString("yyyyMMdd");
        var yesterday = DateTime.Now.AddDays(-1).ToString("yyyyMMdd");
        var now = NowUnix();

        // lê estado atual
        var c = await GetUserCheckinAsync(uid, idToken);

        if (string.Equals(c.LastDateKey, today, StringComparison.OrdinalIgnoreCase))
            return (false, "Você já fez check-in hoje.");

        var nextStreak = string.Equals(c.LastDateKey, yesterday, StringComparison.OrdinalIgnoreCase)
            ? (c.Streak + 1)
            : 1;

        var cycleDay = ((nextStreak - 1) % cycleLen) + 1; // 1..7
        var awardVipDays = (cycleDay == 7) ? 1 : 0;
        var awardCoins = (cycleDay == 7) ? 0 : coinsPerDay;

        // ✅ tudo em um PATCH (reduz chance de "marcou mas não creditou")
        var patch = new Dictionary<string, object?>
        {
            [$"users/{uid}/rewards/checkin/streak"] = nextStreak,
            [$"users/{uid}/rewards/checkin/lastDateKey"] = today,
            [$"users/{uid}/rewards/checkin/rewardCoins"] = awardCoins,
            [$"users/{uid}/rewards/checkin/lastCycleDay"] = cycleDay,
            [$"users/{uid}/rewards/checkin/lastVipDays"] = awardVipDays,
            [$"users/{uid}/rewards/checkin/updatedAtUnix"] = now,

            // opcional: histórico simples (útil pra debug)
            [$"users/{uid}/rewards/checkinHistory/{today}"] = new Dictionary<string, object?>
            {
                ["streak"] = nextStreak,
                ["cycleDay"] = cycleDay,
                ["coins"] = awardCoins,
                ["vipDays"] = awardVipDays,
                ["createdAtUnix"] = now
            }
        };

        if (awardVipDays > 0)
        {
            // VIP day: calcula novo premiumUntil e seta plano premium no mesmo PATCH
            await EnsurePremiumConsistencyAsync(uid, idToken);

            var plan = await GetUserPlanAsync(uid, idToken);
            var until = await GetPremiumUntilUnixAsync(uid, idToken);

            if (plan == "premium" && until == 0)
                return (false, "Você já possui Premium ativo (sem prazo).");

            var baseStart = until > now ? until : now;
            var nextUntil = baseStart + 1L * 24L * 60L * 60L;

            patch[$"users/{uid}/profile/plano"] = "premium";
            patch[$"users/{uid}/profile/premiumUntilUnix"] = nextUntil;
            patch[$"users/{uid}/profile/Plano"] = null;
            patch[$"users/{uid}/profile/PremiumUntilUnix"] = null;

            patch[$"users/{uid}/profile/premiumLastReason"] = $"checkin:day{cycleDay}";
            patch[$"users/{uid}/profile/premiumUpdatedAtUnix"] = now;

            // ledger
            patch[$"users/{uid}/wallet/ledger/{now}_checkin_vip"] = new Dictionary<string, object?>
            {
                ["type"] = "checkin",
                ["deltaCoins"] = 0,
                ["vipDays"] = 1,
                ["reason"] = $"checkin:day{cycleDay}",
                ["createdAtUnix"] = now
            };
        }
        else
        {
            // coins day: atualiza carteira no mesmo PATCH
            var curCoins = await GetUserCoinsAsync(uid, idToken);
            var nextCoins = curCoins + awardCoins;

            patch[$"users/{uid}/wallet/coins"] = nextCoins;
            patch[$"users/{uid}/wallet/updatedAtUnix"] = now;
            patch[$"users/{uid}/wallet/lastReason"] = $"checkin:day{cycleDay}";

            // ledger
            patch[$"users/{uid}/wallet/ledger/{now}_checkin_coins"] = new Dictionary<string, object?>
            {
                ["type"] = "checkin",
                ["deltaCoins"] = awardCoins,
                ["vipDays"] = 0,
                ["reason"] = $"checkin:day{cycleDay}",
                ["createdAtUnix"] = now
            };
        }

        var pr = await PatchAsync("", patch, idToken);
        if (!pr.ok) return pr;

        return (true, "OK");
    }

    // ---------- Shop: buy vip => extends PremiumUntilUnix ----------

    public async Task<(bool ok, string message)> BuyVipWithCoinsAsync(string uid, string shopItemId, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(uid)) return (false, "Uid inválido.");
        if (string.IsNullOrWhiteSpace(shopItemId)) return (false, "Item inválido.");

        await EnsurePremiumConsistencyAsync(uid, idToken);

        var it = await GetAsync<RewardShopItem>($"rewards/shop/{shopItemId}", idToken);
        if (it == null) return (false, "Item não encontrado.");

        var debit = await AddUserCoinsAsync(uid, -it.CostCoins, $"buy:{shopItemId}", idToken);
        if (!debit.ok) return debit;

        var ext = await ExtendPremiumDaysAsync(uid, it.VipDays, $"shop:{shopItemId}", idToken);
        if (!ext.ok)
        {
            await AddUserCoinsAsync(uid, it.CostCoins, $"rollback:{shopItemId}", idToken);
            return ext;
        }

        var now = NowUnix();
        await PutAsync($"users/{uid}/rewards/purchases/{shopItemId}_{now}", new Dictionary<string, object?>
        {
            ["itemId"] = shopItemId,
            ["costCoins"] = it.CostCoins,
            ["vipDays"] = it.VipDays,
            ["createdAtUnix"] = now
        }, idToken);

        return (true, "OK");
    }


    // ============================================================
    // AFFILIATES (compatível com seu BD atual)
    // - pagina.php salva em: affiliates/leads/{CODE}/{pushId}
    // - stats ficam em:      affiliates/stats/{REF_UID}
    // - codes ficam em:      affiliates/codes/{CODE} => { uid, createdAtUnix }
    //
    // IMPORTANTE (seu caso):
    // - Se o app NÃO tiver o code no Register, use o método AUTO abaixo que faz SCAN
    //   em affiliates/leads/* procurando pending com emailLower igual ao email do cadastro.
    //   (MVP – pode ser pesado quando crescer, mas resolve agora.)
    //
    // BONUS:
    // - Ao confirmar (pending->confirmed), credita moedas em users/{refUid}/wallet/coins
    //   com base no admin/affiliateroyalties (fallback para defaults do model).
    // ============================================================

    // -------- Config de royalties (admin) --------

    public async Task<AffiliateRoyaltyConfig> GetAffiliateRoyaltiesAsync(string? idToken = null)
    {
        var cfg = await GetAsync<AffiliateRoyaltyConfig>("admin/affiliateroyalties", idToken);
        return cfg ?? new AffiliateRoyaltyConfig { UpdatedAtUnix = NowUnix() };
    }

    public async Task<(bool ok, string message)> UpsertAffiliateRoyaltiesAsync(AffiliateRoyaltyConfig cfg, string? idToken = null)
    {
        if (cfg == null) return (false, "Config inválida.");
        cfg.UpdatedAtUnix = NowUnix();
        return await PutAsync("admin/affiliateroyalties", cfg, idToken);
    }

    // -------- Models (internos do service) --------

    public sealed class AffiliateUser
    {
        public string Uid { get; set; } = "";
        public string Code { get; set; } = "";
        public string Link { get; set; } = "";
        public long CreatedAtUnix { get; set; }
        public long UpdatedAtUnix { get; set; }
    }

    public sealed class AffiliateCodeRow
    {
        public string Uid { get; set; } = "";
        public long CreatedAtUnix { get; set; }
    }

    public sealed class AffiliateStats
    {
        public int Clicks { get; set; }       // opcional (no app, clicks = pendentes)
        public int Signups { get; set; }      // confirmados
        public int BonusCoins { get; set; }   // acumulado creditado
        public long UpdatedAtUnix { get; set; }
    }

    public sealed class AffiliateLead
    {
        public string Id { get; set; } = "";

        // No seu DB atual, ref geralmente é o CODE (ex: FRASX54Y).
        public string Ref { get; set; } = "";

        public string Source { get; set; } = "web";         // web|app
        public string Status { get; set; } = "pending";     // pending|confirmed

        public string EmailLower { get; set; } = "";
        public string EmailHash { get; set; } = "";

        public bool Downloaded { get; set; } = false;
        public long CreatedAtUnix { get; set; }
        public long ConfirmedAtUnix { get; set; }
        public string ConfirmedUid { get; set; } = "";

        public bool StatusEquals(string s)
            => string.Equals((Status ?? "").Trim(), s, StringComparison.OrdinalIgnoreCase);
    }

    private static long UnixNow() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static string NormalizeEmail(string? email)
        => (email ?? "").Trim().ToLowerInvariant();

    private static string GenerateCode(int len = 8)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // sem 0/O/I/1
        var rng = new Random();
        return new string(Enumerable.Range(0, len).Select(_ => chars[rng.Next(chars.Length)]).ToArray());
    }

    public string BuildAffiliateLandingPageLink(string code)
    {
        code = (code ?? "").Trim().ToUpperInvariant();
        return $"https://izzihub.com.br/pagina.php?ref={Uri.EscapeDataString(code)}";
    }

    /// <summary>
    /// Garante affiliates/users/{uid}, affiliates/codes/{code}, affiliates/stats/{uid}.
    /// Retorna (code, link)
    /// </summary>
    public async Task<(string? code, string? link)> EnsureAffiliateAsync(string uid, string? idToken = null)
    {
        uid = (uid ?? "").Trim();
        if (string.IsNullOrWhiteSpace(uid)) return (null, null);

        var user = await GetAsync<AffiliateUser>($"affiliates/users/{uid}", idToken);
        if (user != null && !string.IsNullOrWhiteSpace(user.Code))
            return (user.Code, user.Link);

        var now = UnixNow();
        var code = GenerateCode(8);

        for (var i = 0; i < 6; i++)
        {
            var exists = await GetAsync<AffiliateCodeRow>($"affiliates/codes/{code}", idToken);
            if (exists == null || string.IsNullOrWhiteSpace(exists.Uid))
                break;

            code = GenerateCode(8);
        }

        // você ainda tem isso salvo, ok manter
        var link = $"https://firebasestorage.googleapis.com/v0/b/dramabox-93e83.firebasestorage.app/o/ref%2F{code}.txt?alt=media";

        var au = new AffiliateUser
        {
            Uid = uid,
            Code = code,
            Link = link,
            CreatedAtUnix = now,
            UpdatedAtUnix = now
        };

        var patch = new Dictionary<string, object?>
        {
            [$"affiliates/users/{uid}"] = au,
            [$"affiliates/codes/{code}"] = new AffiliateCodeRow { Uid = uid, CreatedAtUnix = now },
            [$"affiliates/stats/{uid}"] = new AffiliateStats { BonusCoins = 0, Clicks = 0, Signups = 0, UpdatedAtUnix = now }
        };

        await PatchAsync("", patch, idToken);
        return (code, link);
    }

    public async Task<AffiliateStats?> GetAffiliateStatsAsync(string refUid, string? idToken = null)
    {
        refUid = (refUid ?? "").Trim();
        if (string.IsNullOrWhiteSpace(refUid)) return null;
        return await GetAsync<AffiliateStats>($"affiliates/stats/{refUid}", idToken);
    }

    /// <summary>
    /// No seu BD atual, leads estão em affiliates/leads/{BUCKET}/{pushId}
    /// onde BUCKET normalmente é o CODE.
    /// </summary>
    public async Task<List<AffiliateLead>> GetAffiliateLeadsByBucketAsync(string bucketId, string? idToken = null)
    {
        bucketId = (bucketId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(bucketId)) return new List<AffiliateLead>();

        var dict = await GetAsync<Dictionary<string, AffiliateLead>>($"affiliates/leads/{bucketId}", idToken);
        if (dict == null || dict.Count == 0) return new List<AffiliateLead>();

        var list = new List<AffiliateLead>(dict.Count);
        foreach (var kv in dict)
        {
            var lead = kv.Value ?? new AffiliateLead();
            lead.Id = kv.Key ?? "";
            lead.EmailLower = NormalizeEmail(lead.EmailLower);
            lead.Status = string.IsNullOrWhiteSpace(lead.Status) ? "pending" : lead.Status.Trim();
            list.Add(lead);
        }

        return list;
    }

    // ---- ALIAS p/ compatibilidade com telas antigas ----
    public Task<List<AffiliateLead>> GetAffiliateLeadsAsync(string bucketId, string? idToken = null)
        => GetAffiliateLeadsByBucketAsync(bucketId, idToken);

    /// <summary>
    /// AUTO (o que você precisa AGORA):
    /// - Se tiver affiliateCode, tenta confirmar pelo bucket do CODE (e fallback por UID).
    /// - Se NÃO tiver affiliateCode, faz SCAN em affiliates/leads/* procurando pending por emailLower.
    /// </summary>
    public async Task<(bool ok, string message)> ConfirmAffiliateLeadOnRegisterAutoAsync(
        string? affiliateCodeOrEmpty,
        string registeredEmail,
        string newUserUid,
        string? idToken = null
    )
    {
        var code = (affiliateCodeOrEmpty ?? "").Trim().ToUpperInvariant();
        var emailLower = NormalizeEmail(registeredEmail);
        newUserUid = (newUserUid ?? "").Trim();

        if (string.IsNullOrWhiteSpace(emailLower)) return (false, "email vazio.");
        if (string.IsNullOrWhiteSpace(newUserUid)) return (false, "uid vazio.");

        if (!string.IsNullOrWhiteSpace(code))
            return await ConfirmAffiliateLeadOnRegisterByCodeAsync(code, emailLower, newUserUid, idToken);

        // Sem code => SCAN global (MVP)
        return await ConfirmAffiliateLeadOnRegisterScanAllAsync(emailLower, newUserUid, idToken);
    }

    /// <summary>
    /// Resolve refUid via affiliates/codes/{code}/uid
    /// e confirma o lead PENDENTE casando pelo emailLower no bucket do CODE.
    /// Mantém fallback legado: bucket por UID.
    /// </summary>
    public async Task<(bool ok, string message)> ConfirmAffiliateLeadOnRegisterByCodeAsync(
        string affiliateCode,
        string registeredEmail,
        string newUserUid,
        string? idToken = null
    )
    {
        affiliateCode = (affiliateCode ?? "").Trim().ToUpperInvariant();
        var emailLower = NormalizeEmail(registeredEmail);
        newUserUid = (newUserUid ?? "").Trim();

        if (string.IsNullOrWhiteSpace(affiliateCode)) return (false, "affiliateCode vazio.");
        if (string.IsNullOrWhiteSpace(emailLower)) return (false, "email vazio.");
        if (string.IsNullOrWhiteSpace(newUserUid)) return (false, "newUserUid vazio.");

        var row = await GetAsync<AffiliateCodeRow>($"affiliates/codes/{affiliateCode}", idToken);
        var refUid = (row?.Uid ?? "").Trim();
        if (string.IsNullOrWhiteSpace(refUid)) return (false, "Código inválido (sem uid).");

        // evita auto-indicação
        if (string.Equals(refUid, newUserUid, StringComparison.Ordinal))
            return (false, "Auto indicação ignorada.");

        // 1) bucket correto (CODE)
        var r1 = await ConfirmAffiliateLeadInBucketAsync(
            bucketId: affiliateCode,
            refUid: refUid,
            registeredEmailLower: emailLower,
            newUserUid: newUserUid,
            affiliateCode: affiliateCode,
            idToken: idToken
        );

        if (r1.ok) return r1;

        // 2) fallback legado (UID)
        var r2 = await ConfirmAffiliateLeadInBucketAsync(
            bucketId: refUid,
            refUid: refUid,
            registeredEmailLower: emailLower,
            newUserUid: newUserUid,
            affiliateCode: affiliateCode,
            idToken: idToken
        );

        if (r2.ok) return r2;

        return (false, $"Falhou (bucket CODE e UID). CODE: {r1.message} | UID: {r2.message}");
    }

    /// <summary>
    /// SCAN GLOBAL (MVP):
    /// procura em affiliates/leads/* um lead pending com emailLower.
    /// Quando acha:
    /// - confirma em affiliates/leads/{CODE}/{pushId}
    /// - incrementa affiliates/stats/{refUid}
    /// - credita coins ao afiliado (wallet)
    /// </summary>
    private async Task<(bool ok, string message)> ConfirmAffiliateLeadOnRegisterScanAllAsync(
        string registeredEmailLower,
        string newUserUid,
        string? idToken
    )
    {
        registeredEmailLower = NormalizeEmail(registeredEmailLower);
        newUserUid = (newUserUid ?? "").Trim();

        if (string.IsNullOrWhiteSpace(registeredEmailLower)) return (false, "email vazio.");
        if (string.IsNullOrWhiteSpace(newUserUid)) return (false, "uid vazio.");

        // Estrutura: { CODE: { pushId: lead } }
        var all = await GetAsync<Dictionary<string, Dictionary<string, AffiliateLead>>>("affiliates/leads", idToken);
        if (all == null || all.Count == 0) return (false, "Nenhum bucket em affiliates/leads.");

        string? foundCode = null;
        AffiliateLead? foundLead = null;

        foreach (var bucket in all)
        {
            var code = (bucket.Key ?? "").Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(code)) continue;

            var dict = bucket.Value;
            if (dict == null || dict.Count == 0) continue;

            var match = dict
                .Select(kv =>
                {
                    var l = kv.Value ?? new AffiliateLead();
                    l.Id = kv.Key ?? "";
                    l.EmailLower = NormalizeEmail(l.EmailLower);
                    l.Status = (l.Status ?? "pending").Trim();
                    return l;
                })
                .Where(l => l.StatusEquals("pending") && l.EmailLower == registeredEmailLower)
                .OrderBy(l => l.CreatedAtUnix)
                .FirstOrDefault();

            if (match != null && !string.IsNullOrWhiteSpace(match.Id))
            {
                foundCode = code;
                foundLead = match;
                break;
            }
        }

        if (foundCode == null || foundLead == null)
            return (false, "SCAN: não achei lead pending com esse email em nenhum bucket.");

        var row = await GetAsync<AffiliateCodeRow>($"affiliates/codes/{foundCode}", idToken);
        var refUid = (row?.Uid ?? "").Trim();
        if (string.IsNullOrWhiteSpace(refUid))
            return (false, "SCAN: achei lead, mas affiliates/codes/{code} não tem uid.");

        if (string.Equals(refUid, newUserUid, StringComparison.Ordinal))
            return (false, "SCAN: auto indicação ignorada.");

        return await ConfirmAffiliateLeadInBucketAsync(
            bucketId: foundCode,
            refUid: refUid,
            registeredEmailLower: registeredEmailLower,
            newUserUid: newUserUid,
            affiliateCode: foundCode,
            idToken: idToken
        );
    }

    /// <summary>
    /// Confirma 1 lead PENDENTE em um bucket específico:
    /// affiliates/leads/{bucketId}/{pushId}
    /// e:
    /// - incrementa signups do dono (refUid)
    /// - credita coins na wallet do dono (refUid) e soma bonusCoins em stats
    /// </summary>
    private async Task<(bool ok, string message)> ConfirmAffiliateLeadInBucketAsync(
        string bucketId,
        string refUid,
        string registeredEmailLower,
        string newUserUid,
        string affiliateCode,
        string? idToken
    )
    {
        bucketId = (bucketId ?? "").Trim();
        refUid = (refUid ?? "").Trim();
        registeredEmailLower = NormalizeEmail(registeredEmailLower);
        newUserUid = (newUserUid ?? "").Trim();
        affiliateCode = (affiliateCode ?? "").Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(bucketId)) return (false, "bucketId vazio.");
        if (string.IsNullOrWhiteSpace(refUid)) return (false, "refUid vazio.");
        if (string.IsNullOrWhiteSpace(registeredEmailLower)) return (false, "email vazio.");
        if (string.IsNullOrWhiteSpace(newUserUid)) return (false, "uid vazio.");

        var dict = await GetAsync<Dictionary<string, AffiliateLead>>($"affiliates/leads/{bucketId}", idToken);
        if (dict == null || dict.Count == 0) return (false, "Nenhum lead no bucket.");

        var match = dict
            .Select(kv =>
            {
                var l = kv.Value ?? new AffiliateLead();
                l.Id = kv.Key ?? "";
                l.EmailLower = NormalizeEmail(l.EmailLower);
                l.Status = (l.Status ?? "pending").Trim();
                return l;
            })
            .Where(l => l.StatusEquals("pending") && l.EmailLower == registeredEmailLower)
            .OrderBy(l => l.CreatedAtUnix)
            .FirstOrDefault();

        if (match == null || string.IsNullOrWhiteSpace(match.Id))
            return (false, "Nenhum pendente com esse email.");

        var now = UnixNow();

        // lê stats atual
        var stats = await GetAsync<AffiliateStats>($"affiliates/stats/{refUid}", idToken);
        var newSignups = (stats?.Signups ?? 0) + 1;

        // lê royalties (admin)
        var cfg = await GetAffiliateRoyaltiesAsync(idToken);
        var awardCoins = Math.Max(0, cfg.CoinsPerConfirmedSignup);

        // credita coins no afiliado (wallet)
        // (MVP: read + patch)
        var curCoins = await GetUserCoinsAsync(refUid, idToken);
        var nextCoins = curCoins + awardCoins;

        // soma bonusCoins em stats também
        var newBonusCoins = (stats?.BonusCoins ?? 0) + awardCoins;

        var patch = new Dictionary<string, object?>
        {
            // confirma lead
            [$"affiliates/leads/{bucketId}/{match.Id}/status"] = "confirmed",
            [$"affiliates/leads/{bucketId}/{match.Id}/confirmedAtUnix"] = now,
            [$"affiliates/leads/{bucketId}/{match.Id}/confirmedUid"] = newUserUid,

            // stats do dono do código
            [$"affiliates/stats/{refUid}/signups"] = newSignups,
            [$"affiliates/stats/{refUid}/bonusCoins"] = newBonusCoins,
            [$"affiliates/stats/{refUid}/updatedAtUnix"] = now,

            // wallet do afiliado (dono do código)
            [$"users/{refUid}/wallet/coins"] = nextCoins,
            [$"users/{refUid}/wallet/updatedAtUnix"] = now,
            [$"users/{refUid}/wallet/lastReason"] = $"affiliate:confirmed:{bucketId}",

            // ledger (pra auditoria)
            [$"users/{refUid}/wallet/ledger/{now}_affiliate_confirmed_{match.Id}"] = new Dictionary<string, object?>
            {
                ["type"] = "affiliate",
                ["deltaCoins"] = awardCoins,
                ["reason"] = $"confirmed:{bucketId}",
                ["leadId"] = match.Id,
                ["newUserUid"] = newUserUid,
                ["createdAtUnix"] = now
            },

            // auditoria no novo usuário
            [$"users/{newUserUid}/affiliate/referredByUid"] = refUid,
            [$"users/{newUserUid}/affiliate/referredByCode"] = affiliateCode,
            [$"users/{newUserUid}/affiliate/registeredEmailLower"] = registeredEmailLower,
            [$"users/{newUserUid}/affiliate/leadBucket"] = bucketId,
            [$"users/{newUserUid}/affiliate/leadId"] = match.Id,
            [$"users/{newUserUid}/affiliate/confirmedAtUnix"] = now,
            [$"users/{newUserUid}/affiliate/coinsAwardedToReferrer"] = awardCoins,
        };

        var r = await PatchAsync("", patch, idToken);
        return r.ok ? (true, "OK") : (false, r.message ?? "Falha no Patch.");
    }

}