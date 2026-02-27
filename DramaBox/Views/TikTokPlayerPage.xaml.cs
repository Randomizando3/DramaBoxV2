using CommunityToolkit.Maui.Views;
using DramaBox.Models;
using DramaBox.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace DramaBox.Views;

public partial class TikTokPlayerPage : ContentPage
{
    private readonly SessionService _session;
    private readonly FirebaseDatabaseService _db;
    private readonly CommunityService _community;

    // modos suportados: "feed" (já pronto), "series", "random"
    private readonly string _mode;
    private readonly string _seriesId;

    // start específico quando abrir via Community (ep selecionado)
    private readonly string _startEpisodeId;
    private readonly int _startIndex;

    public ObservableCollection<EpisodeItem> Items { get; } = new();

    private EpisodeItem? _current;
    public EpisodeItem? Current
    {
        get => _current;
        set
        {
            if (_current == value) return;
            _current = value;
            OnPropertyChanged(nameof(Current));
        }
    }

    // overlay aparece só quando pausado
    private bool _isOverlayVisible;
    public bool IsOverlayVisible
    {
        get => _isOverlayVisible;
        set
        {
            if (_isOverlayVisible == value) return;
            _isOverlayVisible = value;
            OnPropertyChanged(nameof(IsOverlayVisible));
        }
    }

    private bool _isPlaying;

    // ===== Swipe pan tracking (fallback) =====
    private double _panTotalY;

    // ===== Random infinite queue =====
    private bool _isAppendingRandom;
    private bool _randomInitialized;
    private readonly HashSet<string> _recentEpisodeIds = new();
    private const int RandomPrefetchThreshold = 3; // quando faltar 3 itens, puxa mais
    private const int RecentMax = 250; // limite para evitar crescer infinito

    // =========================================================
    // A) FEED PRONTO (ex: você monta a fila e abre aqui)
    // =========================================================
    public TikTokPlayerPage(List<CommunityService.EpisodeFeedItem> feed, int startIndex = 0)
    {
        InitializeComponent();

        _session = Resolve<SessionService>() ?? new SessionService();
        _db = Resolve<FirebaseDatabaseService>() ?? new FirebaseDatabaseService(new HttpClient());
        var st = Resolve<FirebaseStorageService>() ?? new FirebaseStorageService(new HttpClient());
        _community = Resolve<CommunityService>() ?? new CommunityService(_db, st, _session);

        _mode = "feed";
        _seriesId = "";
        _startEpisodeId = "";
        _startIndex = Math.Max(0, startIndex);

        BindingContext = this;

        Items.Clear();
        if (feed != null)
        {
            foreach (var x in feed)
            {
                var it = new EpisodeItem
                {
                    SeriesId = x.SeriesId ?? "",
                    CreatorUserId = "",
                    CreatorName = x.CreatorName ?? "Criador",
                    DramaTitle = x.DramaTitle ?? "",
                    DramaCoverUrl = x.DramaCoverUrl ?? "",
                    EpisodeId = x.EpisodeId ?? "",
                    EpisodeNumber = x.EpisodeNumber,
                    EpisodeTitle = x.EpisodeTitle ?? "",
                    VideoUrl = x.VideoUrl ?? ""
                };
                Items.Add(it);

                if (!string.IsNullOrWhiteSpace(it.EpisodeId))
                    TrackRecent(it.EpisodeId);
            }
        }

        Feed.ItemsSource = Items;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (Items.Count == 0) return;

            var pos = _startIndex;
            if (pos >= Items.Count) pos = 0;

            Feed.Position = pos;
            Current = Items[pos];
            PlayCurrent();
        });
    }

    // =========================================================
    // B) ABRIR SÉRIE (Community): fila = episódios da série
    //    startEpisodeId permite abrir no ep clicado.
    // =========================================================
    public TikTokPlayerPage(string seriesId, string startEpisodeId = "")
    {
        InitializeComponent();

        _session = Resolve<SessionService>() ?? new SessionService();
        _db = Resolve<FirebaseDatabaseService>() ?? new FirebaseDatabaseService(new HttpClient());
        var st = Resolve<FirebaseStorageService>() ?? new FirebaseStorageService(new HttpClient());
        _community = Resolve<CommunityService>() ?? new CommunityService(_db, st, _session);

        _mode = "series";
        _seriesId = seriesId ?? "";
        _startEpisodeId = startEpisodeId ?? "";
        _startIndex = 0;

        BindingContext = this;
        Feed.ItemsSource = Items;
    }

    // =========================================================
    // C) RANDOM: fila = vídeos aleatórios (prioriza VIP no premium)
    // =========================================================
    public TikTokPlayerPage(bool random)
    {
        InitializeComponent();

        _session = Resolve<SessionService>() ?? new SessionService();
        _db = Resolve<FirebaseDatabaseService>() ?? new FirebaseDatabaseService(new HttpClient());
        var st = Resolve<FirebaseStorageService>() ?? new FirebaseStorageService(new HttpClient());
        _community = Resolve<CommunityService>() ?? new CommunityService(_db, st, _session);

        _mode = "random";
        _seriesId = "";
        _startEpisodeId = "";
        _startIndex = 0;

        BindingContext = this;
        Feed.ItemsSource = Items;
    }

    private static T? Resolve<T>() where T : class
        => Application.Current?.Handler?.MauiContext?.Services?.GetService(typeof(T)) as T;

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        IsOverlayVisible = false;
        _isPlaying = false;

        if (_mode == "series")
        {
            await LoadSeriesAsync();
            StartAtEpisodeIfAny();
            return;
        }

        if (_mode == "random")
        {
            // inicializa fila random e já deixa "infinito" via prefetch
            if (!_randomInitialized)
            {
                _randomInitialized = true;
                await AppendRandomBatchAsync(clearFirst: true);
            }

            StartAtEpisodeIfAny();
            return;
        }
    }

    protected override void OnDisappearing()
    {
        try { Player?.Stop(); } catch { }
        base.OnDisappearing();
    }

    // =========================
    // Start position
    // =========================
    private void StartAtEpisodeIfAny()
    {
        if (Items.Count == 0) return;

        var pos = 0;

        if (!string.IsNullOrWhiteSpace(_startEpisodeId))
        {
            var idx = Items.ToList().FindIndex(x => x.EpisodeId == _startEpisodeId);
            if (idx >= 0) pos = idx;
        }
        else if (_mode == "feed")
        {
            pos = _startIndex;
            if (pos >= Items.Count) pos = 0;
        }

        Feed.Position = pos;
        Current = Items[pos];
        PlayCurrent();

        _ = EnsureInfiniteRandomAsync(); // se já começar perto do fim
    }

    // =========================
    // Premium do viewer (para priorizar VIP no random)
    // =========================
    private async Task<bool> IsViewerPremiumAsync()
    {
        try
        {
            var uid = _session.UserId ?? "";
            if (string.IsNullOrWhiteSpace(uid)) return false;

            // Ajuste conforme seu schema (mantive como você vinha usando)
            var isPremium = (await _db.GetAsync<bool?>($"users/{uid}/profile/isPremium", _session.IdToken)) == true;
            var plano = (await _db.GetAsync<string>($"users/{uid}/profile/plano", _session.IdToken)) ?? "";

            plano = plano.Trim().ToLowerInvariant();
            if (plano is "premium" or "superpremium" or "vip" or "diamond") return true;

            return isPremium;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> IsSeriesVipAsync(string seriesId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(seriesId)) return false;
            // Ajuste conforme seu schema (mantive como você vinha usando)
            return (await _db.GetAsync<bool?>($"community/series/{seriesId}/creatorIsVip", _session.IdToken)) == true;
        }
        catch
        {
            return false;
        }
    }

    // =========================
    // Load series
    // =========================
    private async Task LoadSeriesAsync()
    {
        Items.Clear();

        try
        {
            if (string.IsNullOrWhiteSpace(_seriesId)) return;

            var s = await _db.GetCommunitySeriesAsync(_seriesId, _session.IdToken);
            var eps = await _db.GetCommunityEpisodesAsync(_seriesId, _session.IdToken);

            foreach (var ep in eps.OrderBy(x => x.Number))
            {
                var it = new EpisodeItem
                {
                    SeriesId = _seriesId,
                    CreatorUserId = s?.CreatorUserId ?? "",
                    CreatorName = s?.CreatorName ?? "Criador",
                    DramaTitle = s?.Title ?? "Série",
                    DramaCoverUrl = s?.CoverUrl ?? "",
                    EpisodeId = ep.Id,
                    EpisodeNumber = ep.Number,
                    EpisodeTitle = ep.Title ?? "",
                    VideoUrl = ep.VideoUrl ?? ""
                };
                Items.Add(it);

                if (!string.IsNullOrWhiteSpace(it.EpisodeId))
                    TrackRecent(it.EpisodeId);
            }
        }
        catch
        {
            Items.Clear();
        }
    }

    // =========================
    // Random infinite batches
    // =========================
    private void TrackRecent(string episodeId)
    {
        if (string.IsNullOrWhiteSpace(episodeId)) return;

        _recentEpisodeIds.Add(episodeId);

        // cap simples pra não crescer infinito
        if (_recentEpisodeIds.Count > RecentMax)
        {
            // limpa metade (MVP)
            var half = _recentEpisodeIds.Take(RecentMax / 2).ToList();
            foreach (var x in half) _recentEpisodeIds.Remove(x);
        }
    }

    private async Task EnsureInfiniteRandomAsync()
    {
        if (_mode != "random") return;
        if (Items.Count == 0) return;

        var pos = Feed.Position;
        if (pos < 0) pos = 0;

        // se está perto do fim, prefetch
        if (Items.Count - 1 - pos <= RandomPrefetchThreshold)
            await AppendRandomBatchAsync(clearFirst: false);
    }

    private async Task AppendRandomBatchAsync(bool clearFirst)
    {
        if (_mode != "random") return;
        if (_isAppendingRandom) return;

        _isAppendingRandom = true;

        try
        {
            if (clearFirst)
            {
                Items.Clear();
                _recentEpisodeIds.Clear();
            }

            var viewerPremium = await IsViewerPremiumAsync();

            // pega séries "random"
            var seriesList = await _db.GetCommunityFeedAsync("random", take: 60, _session.IdToken);

            // pega 1 ep por série (MVP)
            var temp = new List<(bool isVip, EpisodeItem item)>();

            foreach (var s in seriesList)
            {
                var eps = await _db.GetCommunityEpisodesAsync(s.Id, _session.IdToken);

                // escolhe um ep que ainda não foi usado recentemente
                var candidate = eps.OrderBy(x => x.Number).FirstOrDefault(x => !_recentEpisodeIds.Contains(x.Id));
                candidate ??= eps.OrderBy(x => x.Number).FirstOrDefault();
                if (candidate == null) continue;

                var isVip = viewerPremium && await IsSeriesVipAsync(s.Id);

                var it = new EpisodeItem
                {
                    SeriesId = s.Id,
                    CreatorUserId = s.CreatorUserId ?? "",
                    CreatorName = s.CreatorName ?? "Criador",
                    DramaTitle = s.Title ?? "Série",
                    DramaCoverUrl = s.CoverUrl ?? "",
                    EpisodeId = candidate.Id,
                    EpisodeNumber = candidate.Number,
                    EpisodeTitle = candidate.Title ?? "",
                    VideoUrl = candidate.VideoUrl ?? ""
                };

                temp.Add((isVip, it));
            }

            var rnd = new Random();

            IEnumerable<(bool isVip, EpisodeItem item)> ordered = viewerPremium
                ? temp.OrderByDescending(x => x.isVip).ThenBy(_ => rnd.Next())
                : temp.OrderBy(_ => rnd.Next());

            // adiciona no final (infinito)
            foreach (var x in ordered)
            {
                // evita duplicar hard (se vier repetido no batch)
                if (!string.IsNullOrWhiteSpace(x.item.EpisodeId) && _recentEpisodeIds.Contains(x.item.EpisodeId))
                    continue;

                Items.Add(x.item);

                if (!string.IsNullOrWhiteSpace(x.item.EpisodeId))
                    TrackRecent(x.item.EpisodeId);
            }
        }
        catch
        {
            // não zera tudo aqui, senão trava o "infinito"
        }
        finally
        {
            _isAppendingRandom = false;
        }
    }

    // =========================================================
    // Playback control (1 player)
    // =========================================================
    private void PlayCurrent()
    {
        if (Current == null) return;

        try
        {
            Player.Stop();
            Player.Source = Current.VideoUrl;
            Player.Play();

            _isPlaying = true;
            IsOverlayVisible = false;
        }
        catch
        {
            _isPlaying = false;
            IsOverlayVisible = true;
        }
    }

    private void Pause()
    {
        try
        {
            Player.Pause();
            _isPlaying = false;
            IsOverlayVisible = true;
        }
        catch { }
    }

    private void Resume()
    {
        try
        {
            Player.Play();
            _isPlaying = true;
            IsOverlayVisible = false;
        }
        catch { }
    }

    private void TogglePlayPause()
    {
        if (_isPlaying) Pause();
        else Resume();
    }

    private async void OnCurrentItemChanged(object sender, CurrentItemChangedEventArgs e)
    {
        Current = e.CurrentItem as EpisodeItem;
        PlayCurrent();
        await EnsureInfiniteRandomAsync();
    }

    private void OnScreenTapped(object sender, TappedEventArgs e)
        => TogglePlayPause();

    // =========================================================
    // Pan gesture => swipe up/down (fallback robusto)
    // =========================================================
    private async void OnPanUpdated(object sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panTotalY = 0;
                break;

            case GestureStatus.Running:
                _panTotalY = e.TotalY;
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                // swipe pra cima: TotalY negativo grande
                if (_panTotalY < -45)
                {
                    OnNextClicked(this, EventArgs.Empty);
                }
                // opcional: swipe pra baixo volta (se quiser)
                else if (_panTotalY > 45)
                {
                    OnPrevClicked();
                }

                _panTotalY = 0;
                await EnsureInfiniteRandomAsync();
                break;
        }
    }

    private void OnPrevClicked()
    {
        if (Items.Count == 0) return;

        var prev = Feed.Position - 1;
        if (prev < 0) prev = 0;

        if (prev != Feed.Position)
            Feed.Position = prev;
    }

    private async void OnBackClicked(object sender, EventArgs e)
        => await Navigation.PopAsync();

    // Próximo: avança o swipe (fila atual)
    private async void OnNextClicked(object sender, EventArgs e)
    {
        if (Items.Count == 0) return;

        var next = Feed.Position + 1;

        // series: para no último
        // random: se estiver no último, tenta puxar mais e depois anda
        if (next >= Items.Count)
        {
            if (_mode == "random")
            {
                await AppendRandomBatchAsync(clearFirst: false);
                next = Math.Min(Feed.Position + 1, Items.Count - 1);
            }
            else
            {
                next = Items.Count - 1;
            }
        }

        if (next != Feed.Position)
            Feed.Position = next;

        await EnsureInfiniteRandomAsync();
    }

    // Quando termina: vai pro próximo (tiktok)
    private void OnMediaEnded(object sender, EventArgs e)
        => OnNextClicked(sender, e);

    // =========================================================
    // Actions
    // =========================================================
    private async void OnLikeClicked(object sender, EventArgs e)
    {
        var it = Current;
        if (it == null) return;

        var uid = _session.UserId ?? "";
        if (string.IsNullOrWhiteSpace(uid))
        {
            await DisplayAlert("Conta", "Você precisa estar logado.", "OK");
            return;
        }

        var creatorUid = it.CreatorUserId;
        if (string.IsNullOrWhiteSpace(creatorUid))
        {
            var s = await _db.GetCommunitySeriesAsync(it.SeriesId, _session.IdToken);
            creatorUid = s?.CreatorUserId ?? "";
        }

        var (ok, msg, _) = await _db.ToggleCommunityLikeAsync(
            userId: uid,
            seriesId: it.SeriesId,
            creatorUid: creatorUid,
            idToken: _session.IdToken
        );

        if (!ok)
            await DisplayAlert("Curtir", msg, "OK");
    }

    private async void OnShareClicked(object sender, EventArgs e)
    {
        var it = Current;
        if (it == null) return;

        try
        {
            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Title = "Compartilhar",
                Text = $"Assista: {it.DramaTitle} — Ep. {it.EpisodeNumber}: {it.EpisodeTitle}"
            });
        }
        catch { }

        var creatorUid = it.CreatorUserId;
        if (string.IsNullOrWhiteSpace(creatorUid))
        {
            var s = await _db.GetCommunitySeriesAsync(it.SeriesId, _session.IdToken);
            creatorUid = s?.CreatorUserId ?? "";
        }

        await _db.AddCommunityShareAsync(it.SeriesId, creatorUid, _session.IdToken);
    }

    private async void OnPlaylistClicked(object sender, EventArgs e)
    {
        var it = Current;
        if (it == null) return;

        var uid = _session.UserId ?? "";
        if (string.IsNullOrWhiteSpace(uid))
        {
            await DisplayAlert("Conta", "Você precisa estar logado.", "OK");
            return;
        }

        var fakeDrama = new DramaSeries
        {
            Id = it.SeriesId,
            Title = it.DramaTitle,
            Subtitle = it.CreatorName,
            CoverUrl = it.DramaCoverUrl
        };

        var (ok, msg, _) = await _db.TogglePlaylistAsync(uid, fakeDrama, _session.IdToken);
        if (!ok)
            await DisplayAlert("Playlist", msg, "OK");
    }

    public sealed class EpisodeItem
    {
        public string SeriesId { get; set; } = "";
        public string CreatorUserId { get; set; } = "";
        public string CreatorName { get; set; } = "";

        public string DramaTitle { get; set; } = "";
        public string DramaCoverUrl { get; set; } = "";

        public string EpisodeId { get; set; } = "";
        public int EpisodeNumber { get; set; }
        public string EpisodeTitle { get; set; } = "";
        public string VideoUrl { get; set; } = "";
    }
}