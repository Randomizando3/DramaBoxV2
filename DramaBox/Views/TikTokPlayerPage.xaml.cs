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

    private readonly string _mode;   // "feed", "series", "random"
    private readonly string _seriesId;

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
            _ = RefreshInteractionStatesAsync();
        }
    }

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

    private bool _currentIsLiked;
    public bool CurrentIsLiked
    {
        get => _currentIsLiked;
        set { if (_currentIsLiked == value) return; _currentIsLiked = value; OnPropertyChanged(nameof(CurrentIsLiked)); }
    }

    private bool _currentIsSaved;
    public bool CurrentIsSaved
    {
        get => _currentIsSaved;
        set { if (_currentIsSaved == value) return; _currentIsSaved = value; OnPropertyChanged(nameof(CurrentIsSaved)); }
    }

    private int _interactionFetchSeq;

    // ===== Pan / Swipe (igual PlayerPage) =====
    private const double SwipeUpThreshold = 70;
    private const double SwipeDownThreshold = 70;
    private const int SwipeCooldownMs = 450;

    private double _panStartY;
    private bool _panMaybeSwipe;
    private bool _panConsumed;
    private bool _swipeCooldown;

    private DateTime _lastGestureAt = DateTime.MinValue;
    private bool _tapBusy;

    // ===== Random (mantive seu esqueleto) =====
    private bool _isAppendingRandom;
    private bool _randomInitialized;
    private readonly HashSet<string> _recentEpisodeIds = new();
    private const int RandomPrefetchThreshold = 3;
    private const int RecentMax = 250;

    // =========================================================
    // A) FEED PRONTO
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
        Feed.ItemsSource = Items;

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
                TrackRecent(it.EpisodeId);
            }
        }

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (Items.Count == 0) return;

            var pos = _startIndex;
            if (pos >= Items.Count) pos = 0;

            await ScrollToIndexAsync(pos, animate: false);
        });
    }

    // =========================================================
    // B) ABRIR SÉRIE
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
    // C) RANDOM
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
        CurrentIsLiked = false;
        CurrentIsSaved = false;

        if (_mode == "series")
        {
            await LoadSeriesAsync();
            await StartAtEpisodeIfAnyAsync();
            return;
        }

        if (_mode == "random")
        {
            if (!_randomInitialized)
            {
                _randomInitialized = true;
                await AppendRandomBatchAsync(clearFirst: true);
            }
            await StartAtEpisodeIfAnyAsync();
            return;
        }

        if (_mode == "feed")
        {
            if (Items.Count > 0 && Current == null)
                await StartAtEpisodeIfAnyAsync();
            else
                _ = RefreshInteractionStatesAsync();
        }
    }

    protected override void OnDisappearing()
    {
        try { Player?.Stop(); } catch { }
        base.OnDisappearing();
    }

    // =========================
    // Start position (robusto)
    // =========================
    private async Task StartAtEpisodeIfAnyAsync()
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

        await ScrollToIndexAsync(pos, animate: false);
        _ = EnsureInfiniteRandomAsync();
    }

    // ? Este método é o que faz funcionar no Windows
    private async Task ScrollToIndexAsync(int index, bool animate)
    {
        if (Items.Count == 0) return;
        if (index < 0) index = 0;
        if (index >= Items.Count) index = Items.Count - 1;

        var it = Items[index];

        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                Feed.ScrollTo(index, position: ScrollToPosition.Center, animate: animate);
            }
            catch
            {
                // fallback: tenta Position
                try { Feed.Position = index; } catch { }
            }

            Current = it;
            PlayCurrent();
        });

        await Task.CompletedTask;
    }

    // =========================
    // LIKE + SALVOS
    // =========================
    private async Task RefreshInteractionStatesAsync()
    {
        var seq = ++_interactionFetchSeq;

        try
        {
            var it = Current;
            if (it == null)
            {
                CurrentIsLiked = false;
                CurrentIsSaved = false;
                return;
            }

            var uid = (_session.UserId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(uid))
            {
                CurrentIsLiked = false;
                CurrentIsSaved = false;
                return;
            }

            var likePath = $"community/interactions/likes/{uid}/{it.SeriesId}";
            var liked = (await _db.GetAsync<bool?>(likePath, _session.IdToken)) == true;

            var saved = await _db.IsInPlaylistAsync(uid, it.SeriesId, _session.IdToken);

            if (seq != _interactionFetchSeq) return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                CurrentIsLiked = liked;
                CurrentIsSaved = saved;
            });
        }
        catch
        {
            if (seq != _interactionFetchSeq) return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                CurrentIsLiked = false;
                CurrentIsSaved = false;
            });
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

            System.Diagnostics.Debug.WriteLine($"TOTAL EPS: {eps.Count}");

            foreach (var ep in eps.OrderBy(x => x.Number))
            {
                var it = new EpisodeItem
                {
                    SeriesId = _seriesId,
                    CreatorUserId = s?.CreatorUserId ?? "",
                    CreatorName = s?.CreatorName ?? "Criador",
                    DramaTitle = s?.Title ?? "Série",
                    DramaCoverUrl = s?.CoverUrl ?? "",
                    EpisodeId = ep.Id ?? "",
                    EpisodeNumber = ep.Number,
                    EpisodeTitle = ep.Title ?? "",
                    VideoUrl = ep.VideoUrl ?? ""
                };

                Items.Add(it);
                TrackRecent(it.EpisodeId);
            }
        }
        catch
        {
            Items.Clear();
        }
    }

    // =========================
    // Random helpers (mantidos)
    // =========================
    private void TrackRecent(string episodeId)
    {
        if (string.IsNullOrWhiteSpace(episodeId)) return;

        _recentEpisodeIds.Add(episodeId);

        if (_recentEpisodeIds.Count > RecentMax)
        {
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

            var seriesList = await _db.GetCommunityFeedAsync("random", take: 60, _session.IdToken);
            var temp = new List<EpisodeItem>();

            foreach (var s in seriesList)
            {
                if (s == null || string.IsNullOrWhiteSpace(s.Id))
                    continue;

                var eps = await _db.GetCommunityEpisodesAsync(s.Id, _session.IdToken);
                if (eps == null || eps.Count == 0) continue;

                var candidate = eps.OrderBy(x => x.Number).FirstOrDefault(x => !_recentEpisodeIds.Contains(x.Id ?? ""));
                candidate ??= eps.OrderBy(x => x.Number).FirstOrDefault();
                if (candidate == null) continue;

                var it = new EpisodeItem
                {
                    SeriesId = s.Id,
                    CreatorUserId = s.CreatorUserId ?? "",
                    CreatorName = s.CreatorName ?? "Criador",
                    DramaTitle = s.Title ?? "Série",
                    DramaCoverUrl = s.CoverUrl ?? "",
                    EpisodeId = candidate.Id ?? "",
                    EpisodeNumber = candidate.Number,
                    EpisodeTitle = candidate.Title ?? "",
                    VideoUrl = candidate.VideoUrl ?? ""
                };

                temp.Add(it);
            }

            // shuffle
            var rnd = new Random();
            foreach (var it in temp.OrderBy(_ => rnd.Next()))
            {
                if (!string.IsNullOrWhiteSpace(it.EpisodeId) && _recentEpisodeIds.Contains(it.EpisodeId))
                    continue;

                Items.Add(it);
                TrackRecent(it.EpisodeId);
            }
        }
        catch
        {
            // não zera Items
        }
        finally
        {
            _isAppendingRandom = false;
        }
    }

    // =========================================================
    // Playback
    // =========================================================
    private void PlayCurrent()
    {
        if (Current == null) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
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
        });
    }

    private void Pause()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                Player.Pause();
                _isPlaying = false;
                IsOverlayVisible = true;
            }
            catch { }
        });
    }

    private void Resume()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                Player.Play();
                _isPlaying = true;
                IsOverlayVisible = false;
            }
            catch { }
        });
    }

    private void TogglePlayPause()
    {
        if (_tapBusy) return;
        _tapBusy = true;

        try
        {
            if ((DateTime.UtcNow - _lastGestureAt).TotalMilliseconds < 120)
                return;

            if (_isPlaying) Pause();
            else Resume();
        }
        finally
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(120);
                _tapBusy = false;
            });
        }
    }

    private void OnScreenTapped(object sender, TappedEventArgs e)
        => TogglePlayPause();

    private async void OnCurrentItemChanged(object sender, CurrentItemChangedEventArgs e)
    {
        _lastGestureAt = DateTime.UtcNow;

        Current = e.CurrentItem as EpisodeItem;
        PlayCurrent();
        await EnsureInfiniteRandomAsync();
    }

    // =========================================================
    // Pan => swipe up/down (igual PlayerPage)
    // =========================================================
    private async void OnPanUpdated(object sender, PanUpdatedEventArgs e)
    {
        if (_swipeCooldown) return;

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panStartY = e.TotalY;
                _panMaybeSwipe = true;
                _panConsumed = false;
                break;

            case GestureStatus.Running:
                if (!_panMaybeSwipe || _panConsumed) return;

                var deltaY = e.TotalY - _panStartY;

                if (deltaY <= -SwipeUpThreshold)
                {
                    _panConsumed = true;
                    _panMaybeSwipe = false;
                    await TriggerFromSwipeAsync(next: true);
                }
                else if (deltaY >= SwipeDownThreshold)
                {
                    _panConsumed = true;
                    _panMaybeSwipe = false;
                    await TriggerFromSwipeAsync(next: false);
                }
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                _panMaybeSwipe = false;
                _panConsumed = false;
                break;
        }
    }

    private async Task TriggerFromSwipeAsync(bool next)
    {
        try
        {
            _swipeCooldown = true;
            if (next) await GoNextAsync();
            else await GoPrevAsync();
        }
        finally
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(SwipeCooldownMs);
                MainThread.BeginInvokeOnMainThread(() => _swipeCooldown = false);
            });
        }
    }

    // =========================================================
    // Next/Prev (USANDO ScrollToIndexAsync)
    // =========================================================
    private async Task GoPrevAsync()
    {
        if (Items.Count == 0) return;

        var currentIndex = GetCurrentIndexSafe();
        var prev = Math.Max(0, currentIndex - 1);

        if (prev != currentIndex)
            await ScrollToIndexAsync(prev, animate: false);

        await EnsureInfiniteRandomAsync();
    }

    private async Task GoNextAsync()
    {
        if (Items.Count == 0) return;

        var currentIndex = GetCurrentIndexSafe();
        var next = currentIndex + 1;

        if (next >= Items.Count)
        {
            if (_mode == "random")
            {
                await AppendRandomBatchAsync(clearFirst: false);
                next = Math.Min(currentIndex + 1, Items.Count - 1);
            }
            else
            {
                next = Items.Count - 1;
            }
        }

        if (next != currentIndex)
            await ScrollToIndexAsync(next, animate: false);

        await EnsureInfiniteRandomAsync();
    }

    private int GetCurrentIndexSafe()
    {
        try
        {
            var it = Current;
            if (it == null) return Feed.Position < 0 ? 0 : Feed.Position;

            var idx = Items.IndexOf(it);
            if (idx >= 0) return idx;

            var pos = Feed.Position;
            if (pos < 0) pos = 0;
            if (pos >= Items.Count) pos = Items.Count - 1;
            return pos;
        }
        catch
        {
            return 0;
        }
    }

    // =========================================================
    // Buttons
    // =========================================================
    private async void OnBackClicked(object sender, EventArgs e)
        => await Navigation.PopAsync();

    private async void OnNextClicked(object sender, EventArgs e)
        => await GoNextAsync();

    private async void OnPrevClicked(object sender, EventArgs e)
        => await GoPrevAsync();

    private async void OnMediaEnded(object sender, EventArgs e)
        => await GoNextAsync();

    // =========================================================
    // Actions
    // =========================================================
    private async void OnLikeClicked(object sender, EventArgs e)
    {
        var it = Current;
        if (it == null) return;

        var uid = (_session.UserId ?? "").Trim();
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

        var (ok, msg, nowLiked) = await _db.ToggleCommunityLikeAsync(
            userId: uid,
            seriesId: it.SeriesId,
            creatorUid: creatorUid,
            idToken: _session.IdToken
        );

        if (!ok)
        {
            await DisplayAlert("Curtir", msg, "OK");
            return;
        }

        CurrentIsLiked = nowLiked;
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

        var uid = (_session.UserId ?? "").Trim();
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

        var (ok, msg, nowSaved) = await _db.TogglePlaylistAsync(uid, fakeDrama, _session.IdToken);
        if (!ok)
        {
            await DisplayAlert("Salvos", msg, "OK");
            return;
        }

        CurrentIsSaved = nowSaved;
    }

    // =========================================================
    // Model interno
    // =========================================================
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