using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DramaBox.Models;
using DramaBox.Services;
using Microsoft.Maui.ApplicationModel; // Share

namespace DramaBox.Views;

public partial class PlayerPage : ContentPage
{
    private readonly FirebaseDatabaseService _db;
    private readonly SessionService _session;

    private string _dramaId = "";
    private string _dramaTitle = "";
    private string _coverUrl = "";
    private DramaEpisode? _episode;

    private string _url = "";
    private CancellationTokenSource? _loopCts;

    private bool _isPaused;
    private List<DramaEpisode>? _episodesCache;

    public PlayerPage()
    {
        InitializeComponent();

        // garante que TabBar não apareça MESMO dentro do Shell
        Shell.SetTabBarIsVisible(this, false);
        Shell.SetNavBarIsVisible(this, false);

        _db = Resolve<FirebaseDatabaseService>() ?? new FirebaseDatabaseService(new HttpClient());
        _session = Resolve<SessionService>() ?? new SessionService();
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

    // ? evite usar isso no app (não salva continue sem dramaId)
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

        try { Player.Play(); } catch { }
        SetPaused(false);

        // ? salva "continue" imediatamente (posição 0) -> aparece na Playlist mesmo se sair rápido
        _ = UpsertContinueEpisodeOnlyAsync();

        // loop (se você quiser manter progresso; se NÃO quiser, eu te mostro como remover)
        _loopCts?.Cancel();
        _loopCts = new CancellationTokenSource();
        _ = SaveLoopAsync(_loopCts.Token);
    }

    protected override async void OnDisappearing()
    {
        try { _loopCts?.Cancel(); } catch { }

        // salva ao sair
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

    // =============================
    // AÇÕES
    // =============================

    private async void OnAddClicked(object sender, EventArgs e)
    {
        var uid = _session.UserId ?? "";
        if (string.IsNullOrWhiteSpace(uid) || string.IsNullOrWhiteSpace(_dramaId))
            return;

        // precisa do DramaSeries pra usar TogglePlaylistAsync
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
                Uri = _url // opcional
            });
        }
        catch { }
    }

    private async void OnLikeClicked(object sender, EventArgs e)
    {
        // Se for catálogo oficial e você tiver endpoint de like, liga aqui.
        // Por enquanto: só feedback visual rápido.
        await DisplayAlert("Curtir", "?? Curtiu!", "OK");
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

            // acha o próximo por Number
            var next = _episodesCache
                .OrderBy(x => x.Number)
                .FirstOrDefault(x => x.Number > _episode.Number);

            if (next == null)
            {
                // acabou a série
                SetPaused(true);
                await DisplayAlert("Fim", "Você chegou ao último episódio.", "OK");
                return;
            }

            _episode = next;
            Title = next.Title ?? "";
            _url = next.VideoUrl ?? "";

            Player.Source = _url;

            try { Player.Play(); } catch { }
            SetPaused(false);

            // ? grava continue com o novo episódio (posição 0)
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
        // grava só o episódio (posição 0) pra playlist aparecer SEM depender de tempo
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

            // se pausado, não precisa ficar gravando
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

            // se não for force, evita gravar cedo demais
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
}