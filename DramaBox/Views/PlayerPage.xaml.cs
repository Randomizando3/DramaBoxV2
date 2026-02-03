using System;
using System.Threading;
using System.Threading.Tasks;
using DramaBox.Models;
using DramaBox.Services;

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

    public PlayerPage()
    {
        InitializeComponent();

        _db = Resolve<FirebaseDatabaseService>() ?? new FirebaseDatabaseService(new HttpClient());
        _session = Resolve<SessionService>() ?? new SessionService();
    }

    // ? construtor novo (preferido)
    public PlayerPage(string dramaId, string dramaTitle, string coverUrl, DramaEpisode episode) : this()
    {
        _dramaId = dramaId ?? "";
        _dramaTitle = dramaTitle ?? "";
        _coverUrl = coverUrl ?? "";
        _episode = episode;

        Title = episode?.Title ?? "";
        _url = episode?.VideoUrl ?? "";
    }

    // ? mantém compatibilidade com chamadas antigas
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

        // loop de persistência
        _loopCts?.Cancel();
        _loopCts = new CancellationTokenSource();
        _ = SaveLoopAsync(_loopCts.Token);
    }

    protected override async void OnDisappearing()
    {
        try { _loopCts?.Cancel(); } catch { }

        // salva uma última vez ao sair
        await SaveProgressAsync(force: true);

        try { Player?.Stop(); } catch { }
        base.OnDisappearing();
    }

    private async Task SaveLoopAsync(CancellationToken ct)
    {
        // se não tem contexto (dramaId/episode), não salva continue
        if (string.IsNullOrWhiteSpace(_session.UserId) ||
            string.IsNullOrWhiteSpace(_dramaId) ||
            _episode == null)
            return;

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct).ContinueWith(_ => { });

            if (ct.IsCancellationRequested)
                break;

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
            // posição em segundos
            var pos = Player?.Position ?? TimeSpan.Zero;
            var seconds = (long)Math.Max(0, pos.TotalSeconds);

            // se não for "force", evita gravar muito cedo
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
        catch
        {
            // não quebra o player por causa de persistência
        }
    }

    private async void OnBackClicked(object sender, EventArgs e)
        => await Navigation.PopAsync();
}
