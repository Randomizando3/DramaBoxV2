using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using DramaBox.Models;
using DramaBox.Services;

namespace DramaBox.Views;

public partial class PlaylistView : ContentPage
{
    private readonly SessionService _session;
    private readonly FirebaseDatabaseService _db;

    public ObservableCollection<FirebaseDatabaseService.ContinueWatchingItem> ContinueItems { get; } = new();
    public ObservableCollection<FirebaseDatabaseService.PlaylistItem> SavedItems { get; } = new();

    public PlaylistView()
    {
        InitializeComponent();

        _session = Resolve<SessionService>() ?? new SessionService();
        _db = Resolve<FirebaseDatabaseService>() ?? new FirebaseDatabaseService(new HttpClient());

        BindingContext = this;
    }

    private static T? Resolve<T>() where T : class
        => Application.Current?.Handler?.MauiContext?.Services?.GetService(typeof(T)) as T;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var uid = _session.UserId ?? "";
            if (string.IsNullOrWhiteSpace(uid))
            {
                ContinueItems.Clear();
                SavedItems.Clear();
                UpdateEmptyStates();
                return;
            }

            // Continue Assistindo
            var cont = await _db.GetContinueAsync(uid, _session.IdToken, take: 30);
            ContinueItems.Clear();
            foreach (var it in cont)
                ContinueItems.Add(it);

            // Salvos (Playlist)
            var saved = await _db.GetPlaylistAsync(uid, _session.IdToken);
            SavedItems.Clear();
            foreach (var it in saved)
                SavedItems.Add(it);

            UpdateEmptyStates();
        }
        catch
        {
            ContinueItems.Clear();
            SavedItems.Clear();
            UpdateEmptyStates();
        }
    }

    private void UpdateEmptyStates()
    {
        if (ContinueEmpty != null)
            ContinueEmpty.IsVisible = ContinueItems.Count == 0;

        if (SavedEmpty != null)
            SavedEmpty.IsVisible = SavedItems.Count == 0;
    }

    private async void OnRefreshClicked(object sender, EventArgs e)
        => await LoadAsync();

    private async void OnContinuePlayClicked(object sender, EventArgs e)
    {
        if (sender is not BindableObject bo || bo.BindingContext is not FirebaseDatabaseService.ContinueWatchingItem it)
            return;

        // monta um "DramaEpisode" mínimo pra reutilizar PlayerPage
        var ep = new DramaEpisode
        {
            Id = it.EpisodeId,
            Number = it.EpisodeNumber,
            Title = it.EpisodeTitle,
            VideoUrl = it.VideoUrl
        };

        await Navigation.PushAsync(new PlayerPage(
            dramaId: it.DramaId,
            dramaTitle: it.DramaTitle,
            coverUrl: it.DramaCoverUrl,
            episode: ep
        ));
    }

    private async void OnOpenSavedClicked(object sender, EventArgs e)
    {
        if (sender is not BindableObject bo || bo.BindingContext is not FirebaseDatabaseService.PlaylistItem it)
            return;

        var dramaId = (it.DramaId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(dramaId))
            return;

        try
        {
            // ? 1) Tenta tratar como "Community Series"
            // Se existir community/series/{dramaId}, então é série da comunidade.
            var series = await _db.GetCommunitySeriesAsync(dramaId, _session.IdToken);

            if (series != null && !string.IsNullOrWhiteSpace(series.Id))
            {
                var eps = await _db.GetCommunityEpisodesAsync(series.Id, _session.IdToken);

                if (eps != null && eps.Count > 0)
                {
                    // ? Monta o FEED COMPLETO (todos episódios) para swipe funcionar
                    var feed = eps
                        .OrderBy(x => x.Number)
                        .Select(ep => new CommunityService.EpisodeFeedItem
                        {
                            SeriesId = series.Id,
                            CreatorName = series.CreatorName ?? it.Subtitle ?? "Criador",
                            DramaTitle = series.Title ?? it.Title ?? "Série",
                            DramaCoverUrl = series.CoverUrl ?? it.CoverUrl ?? "",
                            EpisodeId = ep.Id ?? "",
                            EpisodeNumber = ep.Number,
                            EpisodeTitle = ep.Title ?? "",
                            VideoUrl = ep.VideoUrl ?? ""
                        })
                        .ToList();

                    // abre no TikTok com o primeiro episódio
                    await Navigation.PushAsync(new TikTokPlayerPage(feed, startIndex: 0));
                    return;
                }

                // Se a série existe mas não trouxe eps, ainda tenta abrir a página de série (vai tentar carregar lá)
                await Navigation.PushAsync(new TikTokPlayerPage(series.Id));
                return;
            }

            // ? 2) Não é community => fluxo padrão (catálogo geral)
            await Navigation.PushAsync(new DramaDetailsPage(dramaId));
        }
        catch
        {
            // fallback seguro
            await Navigation.PushAsync(new DramaDetailsPage(dramaId));
        }
    }

    private async void OnRemoveSavedClicked(object sender, EventArgs e)
    {
        if (sender is not BindableObject bo || bo.BindingContext is not FirebaseDatabaseService.PlaylistItem it)
            return;

        var uid = _session.UserId ?? "";
        if (string.IsNullOrWhiteSpace(uid) || string.IsNullOrWhiteSpace(it.DramaId))
            return;

        var (ok, msg) = await _db.RemoveFromPlaylistAsync(uid, it.DramaId, _session.IdToken);
        if (!ok)
        {
            await DisplayAlert("Playlist", msg, "OK");
            return;
        }

        // remove da UI sem recarregar tudo
        var found = SavedItems.FirstOrDefault(x => x.DramaId == it.DramaId);
        if (found != null)
            SavedItems.Remove(found);

        UpdateEmptyStates();
    }
}