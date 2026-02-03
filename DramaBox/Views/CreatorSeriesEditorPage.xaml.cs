using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DramaBox.Models;
using DramaBox.Services;
using Microsoft.Maui.Storage;

namespace DramaBox.Views;

public partial class CreatorSeriesEditorPage : ContentPage
{
    private readonly SessionService _session;
    private readonly FirebaseDatabaseService _db;
    private readonly FirebaseStorageService _st;
    private readonly string _seriesId;

    private CommunitySeries? _series;

    public ObservableCollection<CommunityEpisode> Episodes { get; } = new();

    public CreatorSeriesEditorPage(string seriesId)
    {
        InitializeComponent();

        _seriesId = seriesId ?? "";

        _session = Resolve<SessionService>() ?? new SessionService();
        _db = Resolve<FirebaseDatabaseService>() ?? new FirebaseDatabaseService(new HttpClient());
        _st = Resolve<FirebaseStorageService>() ?? new FirebaseStorageService(new HttpClient());

        EpisodesList.ItemsSource = Episodes;
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
        if (string.IsNullOrWhiteSpace(_seriesId))
            return;

        _series = await _db.GetCommunitySeriesAsync(_seriesId, _session.IdToken);
        TitleLabel.Text = _series?.Title ?? "Série";
        CoverImage.Source = _series?.CoverUrl ?? _series?.PosterUrl ?? "";

        var eps = await _db.GetCommunityEpisodesAsync(_seriesId, _session.IdToken);

        Episodes.Clear();
        foreach (var ep in eps.OrderBy(x => x.Number))
            Episodes.Add(ep);
    }

    private async void OnBackClicked(object sender, EventArgs e)
        => await Navigation.PopAsync();

    private async void OnRefreshClicked(object sender, EventArgs e)
        => await LoadAsync();

    private async void OnChangeCoverClicked(object sender, EventArgs e)
    {
        var uid = _session.UserId ?? "";
        if (string.IsNullOrWhiteSpace(uid))
        {
            await DisplayAlert("Conta", "Você precisa estar logado.", "OK");
            return;
        }

        if (_series == null)
        {
            await DisplayAlert("Capa", "Série inválida.", "OK");
            return;
        }

        try
        {
            var photo = await MediaPicker.Default.PickPhotoAsync();
            if (photo == null) return;

            var ext = Path.GetExtension(photo.FileName ?? "").ToLowerInvariant();
            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg")
                ext = ".jpg";

            await using var stream = await photo.OpenReadAsync();

            var (okUp, url, msgUp) = await _st.UploadCommunitySeriesCoverAsync(
                stream: stream,
                creatorUid: uid,
                seriesId: _seriesId,
                extension: ext,
                idToken: _session.IdToken
            );

            if (!okUp)
            {
                await DisplayAlert("Capa", msgUp, "OK");
                return;
            }

            _series.CoverUrl = url;

            // salva a série atualizada (reaproveita seu método)
            var (ok, msg) = await _db.UpsertCommunitySeriesAsync(uid, _series, _session.IdToken);
            if (!ok)
            {
                await DisplayAlert("Capa", msg, "OK");
                return;
            }

            CoverImage.Source = url;
        }
        catch (Exception ex)
        {
            await DisplayAlert("Capa", $"Falha ao trocar capa: {ex.Message}", "OK");
        }
    }

    private async void OnAddEpisodeClicked(object sender, EventArgs e)
    {
        var uid = _session.UserId ?? "";
        if (string.IsNullOrWhiteSpace(uid))
        {
            await DisplayAlert("Conta", "Você precisa estar logado.", "OK");
            return;
        }

        if (_series == null)
        {
            await DisplayAlert("Episódio", "Série inválida.", "OK");
            return;
        }

        // 1) número + título
        var numStr = await DisplayPromptAsync("Novo episódio", "Número (ex: 1):", keyboard: Keyboard.Numeric);
        if (string.IsNullOrWhiteSpace(numStr)) return;
        if (!int.TryParse(numStr.Trim(), out var number) || number <= 0)
        {
            await DisplayAlert("Episódio", "Número inválido.", "OK");
            return;
        }

        var title = await DisplayPromptAsync("Novo episódio", "Título do episódio:");
        if (string.IsNullOrWhiteSpace(title)) return;

        // 2) escolher vídeo (mp4)
        try
        {
            var pick = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Selecione o vídeo (mp4)",
                FileTypes = FilePickerFileType.Videos
            });

            if (pick == null) return;

            var ext = Path.GetExtension(pick.FileName ?? "").ToLowerInvariant();
            if (ext != ".mp4")
            {
                await DisplayAlert("Episódio", "Por enquanto aceitamos apenas .mp4.", "OK");
                return;
            }

            var episodeId = Guid.NewGuid().ToString("N");

            await using var videoStream = await pick.OpenReadAsync();

            // 3) upload para Storage
            var (okUp, url, msgUp) = await _st.UploadCommunityEpisodeMp4Async(
                stream: videoStream,
                creatorUid: uid,
                seriesId: _seriesId,
                episodeId: episodeId,
                idToken: _session.IdToken
            );

            if (!okUp)
            {
                await DisplayAlert("Episódio", msgUp, "OK");
                return;
            }

            // 4) salva no RTDB
            var ep = new CommunityEpisode
            {
                Id = episodeId,
                Number = number,
                Title = title.Trim(),
                VideoUrl = url,
                DurationSeconds = 0, // MVP (se depois quiser calcular, a gente faz)
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            var (ok, msg) = await _db.UpsertCommunityEpisodeAsync(uid, _seriesId, ep, _session.IdToken);
            if (!ok)
            {
                await DisplayAlert("Episódio", msg, "OK");
                return;
            }

            await LoadAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Episódio", $"Falha ao adicionar episódio: {ex.Message}", "OK");
        }
    }

    private async void OnPlaySeriesClicked(object sender, EventArgs e)
    {
        // usa seu player da comunidade
        await Navigation.PushAsync(new TikTokPlayerPage(mode: "series", seriesId: _seriesId));
    }
}
