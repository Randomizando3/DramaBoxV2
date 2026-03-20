using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DramaBox.Models;
using DramaBox.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Dispatching;

namespace DramaBox.Views;

public partial class PlayerPage : ContentPage
{
    private readonly FirebaseDatabaseService _db;
    private readonly SessionService _session;
    private readonly SubtitleTrackService _subtitleService;

    private string _dramaId = "";
    private string _dramaTitle = "";
    private string _coverUrl = "";
    private DramaEpisode? _episode;

    private string _url = "";
    private CancellationTokenSource? _loopCts;
    private CancellationTokenSource? _subtitleLoadCts;
    private IDispatcherTimer? _subtitleTimer;
    private SubtitleTrack _subtitleTrack = SubtitleTrack.Empty;
    private string _currentSubtitleText = "";

    private bool _isPaused;
    private List<DramaEpisode>? _episodesCache;

    // =========================
    // Swipe up detection
    // =========================
    private const double SwipeUpThreshold = 70; // px (ajuste fino se quiser)
    private const int SwipeCooldownMs = 450;

    private double _panStartY;
    private bool _panMaybeSwipe;
    private bool _panConsumed;
    private bool _swipeCooldown;

    public PlayerPage()
    {
        InitializeComponent();

        // garante que TabBar não apareça MESMO dentro do Shell
        Shell.SetTabBarIsVisible(this, false);
        Shell.SetNavBarIsVisible(this, false);

        _db = Resolve<FirebaseDatabaseService>() ?? new FirebaseDatabaseService(new HttpClient());
        _session = Resolve<SessionService>() ?? new SessionService();
        _subtitleService = Resolve<SubtitleTrackService>() ?? new SubtitleTrackService(new HttpClient());
    }

    public PlayerPage(string dramaId, string dramaTitle, string coverUrl, DramaEpisode episode) : this()
    {
        _dramaId = dramaId ?? "";
        _dramaTitle = dramaTitle ?? "";
        _coverUrl = coverUrl ?? "";
        _episode = episode;

        Title = episode?.Title ?? "";
        _url = episode?.VideoUrl ?? "";
    }

    // evite usar isso no app (não salva continue sem dramaId)
    public PlayerPage(string title, string url) : this()
    {
        Title = title ?? "";
        _url = url ?? "";
    }

    private static T? Resolve<T>() where T : class
        => Application.Current?.Handler?.MauiContext?.Services?.GetService(typeof(T)) as T;

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (string.IsNullOrWhiteSpace(_url))
            return;

        Player.Source = _url;
        _ = LoadSubtitleTrackAsync(_episode);
        EnsureSubtitleTimer();

        try { Player.Play(); } catch { }
        SetPaused(false);

        // salva "continue" imediatamente (posição 0)
        _ = UpsertContinueEpisodeOnlyAsync();

        _loopCts?.Cancel();
        _loopCts = new CancellationTokenSource();
        _ = SaveLoopAsync(_loopCts.Token);
    }

    protected override async void OnDisappearing()
    {
        try { _loopCts?.Cancel(); } catch { }
        try { _subtitleLoadCts?.Cancel(); } catch { }
        StopSubtitleTimer();
        SetSubtitleText("");

        await SaveProgressAsync(force: true);

        try { Player?.Stop(); } catch { }
        base.OnDisappearing();
    }

    private void SetPaused(bool paused)
    {
        _isPaused = paused;
        if (OverlayControls != null)
            OverlayControls.IsVisible = paused;
    }

    private void TogglePause()
    {
        if (_isPaused)
        {
            try { Player.Play(); } catch { }
            SetPaused(false);
        }
        else
        {
            try { Player.Pause(); } catch { }
            SetPaused(true);
        }
    }

    private void OnVideoTapped(object sender, TappedEventArgs e)
        => TogglePause();

    private async void OnBackClicked(object sender, EventArgs e)
        => await Navigation.PopAsync();

    private async void OnNextClicked(object sender, EventArgs e)
        => await PlayNextAsync();

    private async void OnMediaEnded(object sender, EventArgs e)
        => await PlayNextAsync();

    // =========================================================
    // Swipe Up => Próximo
    // =========================================================
    private async void OnPanUpdated(object sender, PanUpdatedEventArgs e)
    {
        // não deixa swipe disparar em sequência
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

                // deltaY negativo = arrastou para cima
                var deltaY = e.TotalY - _panStartY;

                // Se já passou do threshold, consome e toca próximo
                if (deltaY <= -SwipeUpThreshold)
                {
                    _panConsumed = true;
                    _panMaybeSwipe = false;

                    // IMPORTANTE: não pausar no tap acidental
                    // (consumimos aqui e evitamos efeitos colaterais)
                    await TriggerNextFromSwipeAsync();
                }
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                _panMaybeSwipe = false;
                _panConsumed = false;
                break;
        }
    }

    private async Task TriggerNextFromSwipeAsync()
    {
        try
        {
            _swipeCooldown = true;

            // mesma ação do botão "Próximo"
            await PlayNextAsync();
        }
        finally
        {
            // pequeno cooldown para não disparar várias vezes num único gesto
            _ = Task.Run(async () =>
            {
                await Task.Delay(SwipeCooldownMs);
                MainThread.BeginInvokeOnMainThread(() => _swipeCooldown = false);
            });
        }
    }

    // =============================
    // AÇÕES
    // =============================

    private async void OnAddClicked(object sender, EventArgs e)
    {
        var uid = _session.UserId ?? "";
        if (string.IsNullOrWhiteSpace(uid) || string.IsNullOrWhiteSpace(_dramaId))
            return;

        var drama = await _db.GetDramaAsync(_dramaId, _session.IdToken);
        if (drama == null)
            return;

        var (ok, msg, nowSaved) = await _db.TogglePlaylistAsync(uid, drama, _session.IdToken);
        if (!ok)
            await DisplayAlert("Playlist", msg, "OK");
        else
            await DisplayAlert("Playlist", nowSaved ? "Adicionado na playlist." : "Removido da playlist.", "OK");
    }

    private async void OnShareClicked(object sender, EventArgs e)
    {
        try
        {
            var title = _dramaTitle;
            var epTitle = _episode?.Title ?? Title;
            var text = string.IsNullOrWhiteSpace(title)
                ? $"Assiste esse episódio: {epTitle}"
                : $"Assiste {title} • {epTitle}";

            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Title = "DramaBox",
                Text = text,
                Uri = _url
            });
        }
        catch { }
    }

    private async void OnLikeClicked(object sender, EventArgs e)
    {
        await DisplayAlert("Curtir", "? Curtiu!", "OK");
    }

    // =============================
    // NEXT EP
    // =============================

    private async Task PlayNextAsync()
    {
        if (string.IsNullOrWhiteSpace(_dramaId) || _episode == null)
            return;

        try
        {
            _episodesCache ??= await _db.GetEpisodesAsync(_dramaId, _session.IdToken);

            if (_episodesCache == null || _episodesCache.Count == 0)
                return;

            var next = _episodesCache
                .OrderBy(x => x.Number)
                .FirstOrDefault(x => x.Number > _episode.Number);

            if (next == null)
            {
                SetPaused(true);
                await DisplayAlert("Fim", "Você chegou ao último episódio.", "OK");
                return;
            }

            _episode = next;
            Title = next.Title ?? "";
            _url = next.VideoUrl ?? "";

            Player.Source = _url;
            _ = LoadSubtitleTrackAsync(next);

            try { Player.Play(); } catch { }
            SetPaused(false);

            await UpsertContinueEpisodeOnlyAsync();
        }
        catch
        {
            // não quebra
        }
    }

    // =============================
    // CONTINUE / PROGRESS
    // =============================

    private async Task UpsertContinueEpisodeOnlyAsync()
    {
        if (string.IsNullOrWhiteSpace(_session.UserId) ||
            string.IsNullOrWhiteSpace(_dramaId) ||
            _episode == null)
            return;

        try
        {
            await _db.UpsertContinueWatchingAsync(
                userId: _session.UserId,
                dramaId: _dramaId,
                dramaTitle: _dramaTitle,
                coverUrl: _coverUrl,
                episodeId: _episode.Id ?? "",
                episodeNumber: _episode.Number,
                episodeTitle: _episode.Title ?? "",
                videoUrl: _episode.VideoUrl ?? _url,
                positionSeconds: 0,
                idToken: _session.IdToken
            );
        }
        catch { }
    }

    private async Task SaveLoopAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_session.UserId) ||
            string.IsNullOrWhiteSpace(_dramaId) ||
            _episode == null)
            return;

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct).ContinueWith(_ => { });
            if (ct.IsCancellationRequested) break;

            if (_isPaused) continue;

            await SaveProgressAsync(force: false);
        }
    }

    private async Task SaveProgressAsync(bool force)
    {
        if (string.IsNullOrWhiteSpace(_session.UserId) ||
            string.IsNullOrWhiteSpace(_dramaId) ||
            _episode == null)
            return;

        try
        {
            var pos = Player?.Position ?? TimeSpan.Zero;
            var seconds = (long)Math.Max(0, pos.TotalSeconds);

            if (!force && seconds <= 1)
                return;

            await _db.UpsertContinueWatchingAsync(
                userId: _session.UserId,
                dramaId: _dramaId,
                dramaTitle: _dramaTitle,
                coverUrl: _coverUrl,
                episodeId: _episode.Id ?? "",
                episodeNumber: _episode.Number,
                episodeTitle: _episode.Title ?? "",
                videoUrl: _episode.VideoUrl ?? _url,
                positionSeconds: seconds,
                idToken: _session.IdToken
            );
        }
        catch { }
    }

    private void EnsureSubtitleTimer()
    {
        if (Dispatcher == null)
            return;

        if (_subtitleTimer == null)
        {
            _subtitleTimer = Dispatcher.CreateTimer();
            _subtitleTimer.Interval = TimeSpan.FromMilliseconds(200);
            _subtitleTimer.Tick += (_, _) => UpdateSubtitleOverlay();
        }

        if (!_subtitleTimer.IsRunning)
            _subtitleTimer.Start();
    }

    private void StopSubtitleTimer()
    {
        if (_subtitleTimer?.IsRunning == true)
            _subtitleTimer.Stop();
    }

    private async Task LoadSubtitleTrackAsync(DramaEpisode? episode)
    {
        try { _subtitleLoadCts?.Cancel(); } catch { }

        _subtitleLoadCts = new CancellationTokenSource();
        _subtitleTrack = SubtitleTrack.Empty;
        SetSubtitleText("");

        var subtitleUrl = episode?.SubtitleUrl ?? "";
        if (string.IsNullOrWhiteSpace(subtitleUrl))
            return;

        try
        {
            var track = await _subtitleService.LoadFromUrlAsync(
                subtitleUrl,
                episode?.SubtitleFormat,
                _subtitleLoadCts.Token
            );

            if (_subtitleLoadCts.IsCancellationRequested)
                return;

            _subtitleTrack = track;
            MainThread.BeginInvokeOnMainThread(UpdateSubtitleOverlay);
        }
        catch
        {
            _subtitleTrack = SubtitleTrack.Empty;
            MainThread.BeginInvokeOnMainThread(() => SetSubtitleText(""));
        }
    }

    private void UpdateSubtitleOverlay()
    {
        if (_subtitleTrack == null || !_subtitleTrack.HasCues)
        {
            SetSubtitleText("");
            return;
        }

        var position = Player?.Position ?? TimeSpan.Zero;
        var text = _subtitleTrack.GetTextAt(position);
        SetSubtitleText(text);
    }

    private void SetSubtitleText(string? text)
    {
        var normalized = (text ?? "").Trim();
        if (string.Equals(_currentSubtitleText, normalized, StringComparison.Ordinal))
            return;

        _currentSubtitleText = normalized;

        if (SubtitleTextLabel != null)
            SubtitleTextLabel.Text = normalized;

        if (SubtitleOverlay != null)
            SubtitleOverlay.IsVisible = !string.IsNullOrWhiteSpace(normalized);
    }
}
