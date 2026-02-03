using System;
using System.Collections.ObjectModel;
using System.Linq;
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

        // Usa o construtor "novo" que salva progresso
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

        if (string.IsNullOrWhiteSpace(it.DramaId))
            return;

        await Navigation.PushAsync(new DramaDetailsPage(it.DramaId));
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
