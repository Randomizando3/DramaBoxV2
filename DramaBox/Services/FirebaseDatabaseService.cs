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
        // ✅ PATCH no root é válido quando queremos multi-path updates
        // Ex: PatchAsync("", new Dictionary<string, object?> { ["a/b"]=1, ["c/d"]=2 })
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

    private static long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    // ----------------------------
    // Royalties (admin)
    // /admin/communityroyalties
    // ----------------------------
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

    // ----------------------------
    // Criador: séries/episódios
    // /community/series/{seriesId}
    // /community/series/{seriesId}/episodes/{episodeId}
    // índice: /users/{uid}/createdSeries/{seriesId} = true
    // ----------------------------

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

        // salva série + índice do criador
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

        // salva episódio
        var (ok, msg) = await PutAsync($"community/series/{seriesId}/episodes/{ep.Id}", ep, idToken);
        if (!ok) return (ok, msg);

        // atualiza updatedAt da série (pra feed)
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

        // pega ids
        var map = await GetAsync<Dictionary<string, bool>>($"users/{creatorUid}/createdSeries", idToken)
                  ?? new Dictionary<string, bool>();

        var ids = map.Where(kv => kv.Value).Select(kv => kv.Key).ToList();
        if (ids.Count == 0) return new();

        // baixa as séries uma a uma (MVP). Depois dá pra otimizar.
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

    // ----------------------------
    // Feed da Comunidade
    // recommended/popular/random
    // ----------------------------
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
            // shuffle simples
            var rng = new Random();
            return all.OrderBy(_ => rng.Next()).Take(take).ToList();
        }

        // para popular/recommended, usamos métricas
        var metrics = await GetAsync<Dictionary<string, CommunitySeriesMetrics>>("community/metrics/series", idToken)
                      ?? new Dictionary<string, CommunitySeriesMetrics>();

        double Score(CommunitySeries s)
        {
            // fallback
            metrics.TryGetValue(s.Id, out var m);
            m ??= new CommunitySeriesMetrics();

            // "popular": peso likes + shares + minutes
            // "recommended": favorece updatedAt + minutes (mais retenção)
            if (mode == "recommended" || mode == "recomendados")
            {
                return (m.MinutesWatched * 2.0) + (m.Likes * 1.0) + (m.Shares * 1.5) + (s.UpdatedAtUnix / 1_000_000.0);
            }
            else
            {
                // popular
                return (m.MinutesWatched * 1.5) + (m.Likes * 1.0) + (m.Shares * 2.0);
            }
        }

        return all
            .OrderByDescending(Score)
            .Take(take)
            .ToList();
    }

    // ----------------------------
    // Métricas + Earnings helpers
    // ----------------------------
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

        // total creator
        var totalPath = $"community/earnings/creators/{creatorUid}/centsTotal";
        var seriesPath = $"community/earnings/creators/{creatorUid}/series/{seriesId}/centsTotal";
        var updatedPath = $"community/earnings/creators/{creatorUid}/updatedAtUnix";
        var updatedSeriesPath = $"community/earnings/creators/{creatorUid}/series/{seriesId}/updatedAtUnix";

        // read-modify-write (MVP)
        var curTotal = await GetAsync<double?>(totalPath, idToken) ?? 0.0;
        var curSeries = await GetAsync<double?>(seriesPath, idToken) ?? 0.0;

        curTotal += centsDelta;
        curSeries += centsDelta;

        // não deixa negativo
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

    // ----------------------------
    // Like (toggle) + royalties
    // /community/interactions/likes/{userId}/{seriesId}
    // ----------------------------
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

        // ajusta métricas
        var m = await GetSeriesMetricsAsync(seriesId, idToken);

        if (already)
        {
            // unlike
            var del = await DeleteAsync(likePath, idToken);
            if (!del.ok) return (false, del.message, true);

            m.Likes = Math.Max(0, m.Likes - 1);
            await SaveSeriesMetricsAsync(seriesId, m, idToken);

            // reverte earnings do criador
            await AddEarningsAsync(creatorUid, seriesId, -cfg.PerLike, idToken);

            return (true, "OK", false);
        }
        else
        {
            // like
            var put = await PutAsync(likePath, true, idToken);
            if (!put.ok) return (false, put.message, false);

            m.Likes += 1;
            await SaveSeriesMetricsAsync(seriesId, m, idToken);

            await AddEarningsAsync(creatorUid, seriesId, cfg.PerLike, idToken);

            return (true, "OK", true);
        }
    }

    // ----------------------------
    // Share (cada clique soma) + royalties
    // ----------------------------
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

    // ----------------------------
    // WatchSeconds (idempotente por delta) + royalties
    // /community/interactions/watchSeconds/{userId}/{seriesId}/{episodeId} = totalSeconds
    // incrementa MinutesWatched e earnings pelo delta
    // ----------------------------
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

        // grava novo total
        var put = await PutAsync(path, totalSeconds, idToken);
        if (!put.ok) return (false, put.message);

        // converte delta para minutos
        var deltaMinutes = deltaSeconds / 60.0;

        var m = await GetSeriesMetricsAsync(seriesId, idToken);
        m.MinutesWatched += deltaMinutes;
        await SaveSeriesMetricsAsync(seriesId, m, idToken);

        var cfg = await GetCommunityRoyaltiesAsync(idToken);
        var centsDelta = deltaMinutes * cfg.PerMinuteWatched;

        await AddEarningsAsync(creatorUid, seriesId, centsDelta, idToken);

        return (true, "OK");
    }
}
