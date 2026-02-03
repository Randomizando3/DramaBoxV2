// Services/CommunityService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DramaBox.Models;

namespace DramaBox.Services;

public sealed class CommunityService
{
    private readonly FirebaseDatabaseService _db;
    private readonly FirebaseStorageService _st;
    private readonly SessionService _session;

    public CommunityService(FirebaseDatabaseService db, FirebaseStorageService st, SessionService session)
    {
        _db = db;
        _st = st;
        _session = session;
    }

    private static long ToLongSafe(double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) return 0;
        if (v < 0) return 0;
        if (v > long.MaxValue) return long.MaxValue;
        return (long)Math.Round(v);
    }

    private static long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    // =============================
    // DTOs simples p/ UI
    // =============================

    public sealed class CommunityFeedItem
    {
        public string SeriesId { get; set; } = "";
        public string Title { get; set; } = "";
        public string CoverUrl { get; set; } = "";
        public string CreatorName { get; set; } = "";

        public long MetricsLikes { get; set; }
        public long MetricsShares { get; set; }
        public long MetricsMinutesWatched { get; set; }
    }

    public sealed class CreatorSeriesItem
    {
        public string SeriesId { get; set; } = "";
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string CoverUrl { get; set; } = "";
        public int EpisodesCount { get; set; }
    }

    public sealed class CreatorDashboard
    {
        public long TotalLikes { get; set; }
        public long TotalShares { get; set; }
        public long TotalMinutesWatched { get; set; }
        public long RevenueCents { get; set; }
    }

    public sealed class CommunitySeriesDto
    {
        public string SeriesId { get; set; } = "";
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string CoverUrl { get; set; } = "";
        public string CreatorUserId { get; set; } = "";
        public string CreatorName { get; set; } = "";
        public bool CreatorIsVip { get; set; }
    }

    public sealed class CommunityEpisodeDto
    {
        public string EpisodeId { get; set; } = "";
        public int Number { get; set; }
        public string Title { get; set; } = "";
        public string VideoUrl { get; set; } = "";
        public int DurationSeconds { get; set; }
    }

    public sealed class EpisodeFeedItem
    {
        public string SeriesId { get; set; } = "";
        public string DramaId { get; set; } = ""; // compat com playlist/continue
        public string DramaTitle { get; set; } = "";
        public string DramaCoverUrl { get; set; } = "";
        public string CreatorName { get; set; } = "";

        public string EpisodeId { get; set; } = "";
        public int EpisodeNumber { get; set; }
        public string EpisodeTitle { get; set; } = "";
        public string VideoUrl { get; set; } = "";
        public int DurationSeconds { get; set; }
    }

    // =============================
    // FEED (usa seu FirebaseDatabaseService que você já estendeu)
    // =============================
    public async Task<List<CommunityFeedItem>> GetCommunityFeedAsync(string tab)
    {
        tab = (tab ?? "").Trim().ToLowerInvariant();

        // Usando seu método do FirebaseDatabaseService (novo)
        var list = await _db.GetCommunityFeedAsync(tab, take: 60, _session.IdToken);

        // métricas ficam em /community/metrics/series/{seriesId} (no seu db service)
        var metricsMap = await _db.GetAsync<Dictionary<string, CommunitySeriesMetrics>>("community/metrics/series", _session.IdToken)
                        ?? new Dictionary<string, CommunitySeriesMetrics>();

        return list.Select(s =>
        {
            metricsMap.TryGetValue(s.Id, out var m);
            m ??= new CommunitySeriesMetrics();

            return new CommunityFeedItem
            {
                SeriesId = s.Id,
                Title = s.Title ?? "",
                CoverUrl = s.CoverUrl ?? "",
                CreatorName = s.CreatorName ?? "",
                MetricsLikes = ToLongSafe(m.Likes),
                MetricsShares = ToLongSafe(m.Shares),
                MetricsMinutesWatched = ToLongSafe(m.MinutesWatched),
            };
        }).ToList();
    }

    public async Task<CommunitySeriesDto?> GetSeriesAsync(string seriesId)
    {
        if (string.IsNullOrWhiteSpace(seriesId)) return null;

        var s = await _db.GetCommunitySeriesAsync(seriesId, _session.IdToken);
        if (s == null) return null;

        return new CommunitySeriesDto
        {
            SeriesId = s.Id ?? seriesId,
            Title = s.Title ?? "",
            Subtitle = s.Subtitle ?? "",
            CoverUrl = s.CoverUrl ?? "",
            CreatorUserId = s.CreatorUserId ?? "",
            CreatorName = s.CreatorName ?? "",
            CreatorIsVip = s.CreatorIsVip
        };
    }

    public async Task<List<CommunityEpisodeDto>> GetEpisodesAsync(string seriesId)
    {
        var eps = await _db.GetCommunityEpisodesAsync(seriesId, _session.IdToken);

        return eps.Select(ep => new CommunityEpisodeDto
        {
            EpisodeId = ep.Id ?? "",
            Number = ep.Number,
            Title = ep.Title ?? "",
            VideoUrl = ep.VideoUrl ?? "",
            DurationSeconds = ep.DurationSeconds
        })
        .OrderBy(x => x.Number)
        .ToList();
    }

    public async Task<List<EpisodeFeedItem>> BuildEpisodeFeedFromSeriesAsync(string seriesId)
    {
        var s = await GetSeriesAsync(seriesId);
        if (s == null) return new();

        var eps = await GetEpisodesAsync(seriesId);

        return eps.Select(ep => new EpisodeFeedItem
        {
            SeriesId = seriesId,
            DramaId = seriesId, // MVP: dramaId == seriesId
            DramaTitle = s.Title,
            DramaCoverUrl = s.CoverUrl,
            CreatorName = s.CreatorName,

            EpisodeId = ep.EpisodeId,
            EpisodeNumber = ep.Number,
            EpisodeTitle = ep.Title,
            VideoUrl = ep.VideoUrl,
            DurationSeconds = ep.DurationSeconds
        }).ToList();
    }

    public async Task<List<EpisodeFeedItem>> GetRandomEpisodeFeedAsync(int take = 50)
    {
        if (take <= 0) take = 50;

        // pega séries publicadas
        var series = await _db.GetCommunityFeedAsync("random", take: 80, _session.IdToken);
        if (series.Count == 0) return new();

        var rng = new Random();
        var shuffled = series.OrderBy(_ => rng.Next()).ToList();

        var feed = new List<EpisodeFeedItem>(take);

        foreach (var s in shuffled)
        {
            var eps = await GetEpisodesAsync(s.Id);

            foreach (var ep in eps.OrderBy(x => x.Number))
            {
                feed.Add(new EpisodeFeedItem
                {
                    SeriesId = s.Id,
                    DramaId = s.Id,
                    DramaTitle = s.Title ?? "",
                    DramaCoverUrl = s.CoverUrl ?? "",
                    CreatorName = s.CreatorName ?? "",

                    EpisodeId = ep.EpisodeId,
                    EpisodeNumber = ep.Number,
                    EpisodeTitle = ep.Title,
                    VideoUrl = ep.VideoUrl,
                    DurationSeconds = ep.DurationSeconds
                });

                if (feed.Count >= take)
                    return feed;
            }
        }

        return feed;
    }

    public async Task<List<CreatorSeriesItem>> GetCreatorSeriesAsync(string creatorUid)
    {
        if (string.IsNullOrWhiteSpace(creatorUid)) return new();

        var list = await _db.GetCreatorCommunitySeriesAsync(creatorUid, _session.IdToken);

        var result = new List<CreatorSeriesItem>();

        foreach (var s in list)
        {
            var eps = await _db.GetCommunityEpisodesAsync(s.Id, _session.IdToken);

            result.Add(new CreatorSeriesItem
            {
                SeriesId = s.Id,
                Title = s.Title ?? "",
                Subtitle = s.Subtitle ?? "",
                CoverUrl = s.CoverUrl ?? "",
                EpisodesCount = eps.Count
            });
        }

        return result;
    }

    public async Task<CreatorDashboard> GetCreatorDashboardAsync(string creatorUid)
    {
        if (string.IsNullOrWhiteSpace(creatorUid))
            return new CreatorDashboard();

        var list = await _db.GetCreatorCommunitySeriesAsync(creatorUid, _session.IdToken);

        var metricsMap = await _db.GetAsync<Dictionary<string, CommunitySeriesMetrics>>("community/metrics/series", _session.IdToken)
                        ?? new Dictionary<string, CommunitySeriesMetrics>();

        // ganhos já acumulados em /community/earnings/creators/{uid}/centsTotal
        var centsTotal = await _db.GetAsync<double?>($"community/earnings/creators/{creatorUid}/centsTotal", _session.IdToken) ?? 0.0;

        double likes = 0, shares = 0, minutes = 0;

        foreach (var s in list)
        {
            if (metricsMap.TryGetValue(s.Id, out var m) && m != null)
            {
                likes += m.Likes;
                shares += m.Shares;
                minutes += m.MinutesWatched;
            }
        }

        return new CreatorDashboard
        {
            TotalLikes = ToLongSafe(likes),
            TotalShares = ToLongSafe(shares),
            TotalMinutesWatched = ToLongSafe(minutes),
            RevenueCents = ToLongSafe(centsTotal)
        };
    }

    // =============================
    // CRIAÇÃO (série + episódio)
    // =============================

    // Wrapper para telas que esperam (bool ok, string message)
    public async Task<(bool ok, string message)> CreateSeriesAsync(string title, string subtitle, string coverUrl)
    {
        var (ok, msg, _) = await CreateSeriesWithIdAsync(title, subtitle, coverUrl);
        return (ok, msg);
    }

    // Retorna o seriesId quando você precisar
    public async Task<(bool ok, string message, string seriesId)> CreateSeriesWithIdAsync(string title, string subtitle, string coverUrl)
    {
        var uid = _session.UserId ?? "";
        if (string.IsNullOrWhiteSpace(uid))
            return (false, "Você precisa estar logado.", "");

        // ✅ Corrigido: seu perfil usa "Nome" (não Name)
        var creatorName = _session.Profile?.Nome ?? _session.Email ?? "Criador";
        var creatorVip = string.Equals(_session.Profile?.Plano, "premium", StringComparison.OrdinalIgnoreCase);

        var now = NowUnix();

        var series = new CommunitySeries
        {
            Id = Guid.NewGuid().ToString("N"),
            CreatorUserId = uid,
            CreatorName = creatorName,
            CreatorIsVip = creatorVip,

            Title = title ?? "",
            Subtitle = subtitle ?? "",
            CoverUrl = coverUrl ?? "",
            IsPublished = true,
            CreatedAtUnix = now,
            UpdatedAtUnix = now
        };

        var r = await _db.UpsertCommunitySeriesAsync(uid, series, _session.IdToken);
        if (!r.ok) return (false, r.message, "");

        return (true, "OK", series.Id);
    }

    public async Task<(bool ok, string message, string episodeId)> AddEpisodeAsync(
        string seriesId,
        int number,
        string title,
        string videoUrl,
        int durationSeconds
    )
    {
        var uid = _session.UserId ?? "";
        if (string.IsNullOrWhiteSpace(uid))
            return (false, "Você precisa estar logado.", "");

        if (string.IsNullOrWhiteSpace(seriesId))
            return (false, "Série inválida.", "");

        if (string.IsNullOrWhiteSpace(videoUrl) || !videoUrl.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
            return (false, "O vídeo deve ser .mp4.", "");

        var ep = new CommunityEpisode
        {
            Id = Guid.NewGuid().ToString("N"),
            Number = number <= 0 ? 1 : number,
            Title = title ?? "",
            VideoUrl = videoUrl ?? "",
            DurationSeconds = Math.Max(0, durationSeconds),
            CreatedAtUnix = NowUnix()
        };

        var r = await _db.UpsertCommunityEpisodeAsync(uid, seriesId, ep, _session.IdToken);
        if (!r.ok) return (false, r.message, "");

        return (true, "OK", ep.Id);
    }

    // =============================
    // Interações (delegando ao db service)
    // =============================

    public async Task<(bool ok, string message, bool nowLiked)> ToggleLikeAsync(string seriesId, string creatorUid)
    {
        var uid = _session.UserId ?? "";
        if (string.IsNullOrWhiteSpace(uid))
            return (false, "Você precisa estar logado.", false);

        return await _db.ToggleCommunityLikeAsync(uid, seriesId, creatorUid, _session.IdToken);
    }

    public async Task<(bool ok, string message)> AddShareAsync(string seriesId, string creatorUid)
    {
        return await _db.AddCommunityShareAsync(seriesId, creatorUid, _session.IdToken);
    }

    public async Task<(bool ok, string message)> UpsertWatchSecondsAsync(
        string seriesId,
        string episodeId,
        string creatorUid,
        long totalSeconds
    )
    {
        var uid = _session.UserId ?? "";
        if (string.IsNullOrWhiteSpace(uid))
            return (false, "Você precisa estar logado.");

        return await _db.UpsertCommunityWatchSecondsAsync(uid, seriesId, episodeId, creatorUid, totalSeconds, _session.IdToken);
    }
}
