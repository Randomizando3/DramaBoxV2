using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using DramaBox.Models;
using DramaBox.Services;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace DramaBox.Views;

public partial class DramaDetailsPage : ContentPage
{
    private readonly FirebaseDatabaseService _db;
    private readonly SessionService _session;

    private readonly string _dramaId;

    private DramaSeries? _drama;

    private bool _liked;
    private bool _inPlaylist;

    public ObservableCollection<DramaEpisode> Episodes { get; } = new();

    public DramaDetailsPage(string dramaId)
    {
        InitializeComponent();

        _db = Resolve<FirebaseDatabaseService>() ?? new FirebaseDatabaseService(new HttpClient());
        _session = Resolve<SessionService>() ?? new SessionService();

        _dramaId = dramaId ?? "";

        EpisodesList.ItemsSource = Episodes;
    }

    private static T? Resolve<T>() where T : class
        => Application.Current?.Handler?.MauiContext?.Services?.GetService(typeof(T)) as T;

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (string.IsNullOrWhiteSpace(_dramaId))
            return;

        try
        {
            _drama = await _db.GetDramaAsync(_dramaId, _session.IdToken);

            TitleLabel.Text = _drama?.Title ?? "Detalhes";
            SubtitleLabel.Text = _drama?.Subtitle ?? "";
            CoverImage.Source = _drama?.PosterUrl ?? _drama?.CoverUrl ?? "";

            VipBadge.IsVisible = _drama?.IsVip == true;

            var eps = await _db.GetEpisodesAsync(_dramaId, _session.IdToken);
            Episodes.Clear();
            foreach (var ep in eps)
                Episodes.Add(ep);

            await LoadUserFlagsAsync();
            RefreshButtons();
        }
        catch
        {
            Episodes.Clear();
            _liked = false;
            _inPlaylist = false;
            RefreshButtons();
        }
    }

    private async Task LoadUserFlagsAsync()
    {
        _liked = false;
        _inPlaylist = false;

        var uid = _session.UserId ?? "";
        if (string.IsNullOrWhiteSpace(uid))
            return;

        // like: bool
        var likeVal = await _db.GetAsync<bool?>($"users/{uid}/likes/{_dramaId}", _session.IdToken);
        _liked = likeVal == true;

        // playlist: objeto PlaylistItem (não bool)
        _inPlaylist = await _db.IsInPlaylistAsync(uid, _dramaId, _session.IdToken);
    }

    private void RefreshButtons()
    {
        // Esses nomes existem no seu XAML:
        // LikeIcon, LikeText, PlaylistIcon, PlaylistText

        LikeIcon.Text = _liked ? "?" : "?";
        LikeText.Text = _liked ? "Curtido" : "Curtir";

        PlaylistIcon.Text = _inPlaylist ? "?" : "?";
        PlaylistText.Text = _inPlaylist ? "Salvo" : "Playlist";
    }

    private async void OnBackClicked(object sender, EventArgs e)
        => await Navigation.PopAsync();

    private async void OnLikeClicked(object sender, EventArgs e)
    {
        var uid = _session.UserId ?? "";
        if (string.IsNullOrWhiteSpace(uid))
        {
            await DisplayAlert("Conta", "Você precisa estar logado.", "OK");
            return;
        }

        var next = !_liked;

        _liked = next;
        RefreshButtons();

        var (ok, msg) = await _db.PutAsync($"users/{uid}/likes/{_dramaId}", _liked, _session.IdToken);
        if (!ok)
        {
            _liked = !next;
            RefreshButtons();
            await DisplayAlert("Curtir", msg, "OK");
        }
    }

    private async void OnPlaylistClicked(object sender, EventArgs e)
    {
        var uid = _session.UserId ?? "";
        if (string.IsNullOrWhiteSpace(uid))
        {
            await DisplayAlert("Conta", "Você precisa estar logado.", "OK");
            return;
        }

        // precisa do drama carregado pra salvar title/cover no PlaylistItem
        if (_drama == null)
            _drama = await _db.GetDramaAsync(_dramaId, _session.IdToken);

        if (_drama == null || string.IsNullOrWhiteSpace(_drama.Id))
        {
            await DisplayAlert("Playlist", "Drama inválido (sem Id).", "OK");
            return;
        }

        // grava PlaylistItem / remove nó
        var (ok, msg, nowSaved) = await _db.TogglePlaylistAsync(uid, _drama, _session.IdToken);
        if (!ok)
        {
            await DisplayAlert("Playlist", msg, "OK");
            return;
        }

        _inPlaylist = nowSaved;
        RefreshButtons();
    }

    private async void OnShareClicked(object sender, EventArgs e)
    {
        var title = _drama?.Title ?? "DramaBox";
        var subtitle = _drama?.Subtitle ?? "";
        var cover = _drama?.PosterUrl ?? _drama?.CoverUrl ?? "";

        var text = string.IsNullOrWhiteSpace(subtitle)
            ? $"Assista: {title}"
            : $"Assista: {title} — {subtitle}";

        try
        {
            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Title = "Compartilhar drama",
                Text = text,
                Uri = string.IsNullOrWhiteSpace(cover) ? null : cover
            });
        }
        catch
        {
            await DisplayAlert("Compartilhar", "Não foi possível abrir o compartilhamento neste dispositivo.", "OK");
        }
    }

    private async void OnEpisodeSelected(object sender, SelectionChangedEventArgs e)
    {
        var ep = e.CurrentSelection?.FirstOrDefault() as DramaEpisode;
        ((CollectionView)sender).SelectedItem = null;

        if (ep == null)
            return;

        // ? Recomendo passar o contexto completo pro player, pra salvar progresso
        if (_drama == null)
            _drama = await _db.GetDramaAsync(_dramaId, _session.IdToken);

        var cover = _drama?.CoverUrl ?? _drama?.PosterUrl ?? "";

        await Navigation.PushAsync(new PlayerPage(
            dramaId: _dramaId,
            dramaTitle: _drama?.Title ?? "",
            coverUrl: cover,
            episode: ep
        ));
    }
}
