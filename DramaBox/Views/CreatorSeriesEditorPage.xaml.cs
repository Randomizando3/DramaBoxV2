using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using DramaBox.Models;
using DramaBox.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

namespace DramaBox.Views;

public partial class CreatorSeriesEditorPage : ContentPage
{
    private readonly SessionService _session;
    private readonly FirebaseDatabaseService _db;
    private readonly FirebaseStorageService _st;
    private readonly CommunityService _community;
    private readonly string _seriesId;

    private CommunitySeries? _series;

    public ObservableCollection<CommunityEpisode> Episodes { get; } = new();

    private bool _isUploading;

    public CreatorSeriesEditorPage(string seriesId)
    {
        InitializeComponent();

        _seriesId = seriesId ?? "";

        _session = Resolve<SessionService>() ?? new SessionService();
        _db = Resolve<FirebaseDatabaseService>() ?? new FirebaseDatabaseService(new HttpClient());
        _st = Resolve<FirebaseStorageService>() ?? new FirebaseStorageService(new HttpClient());
        _community = Resolve<CommunityService>() ?? new CommunityService(_db, _st, _session);

        BindingContext = this;

        UploadTopBar.IsVisible = false;
        UploadProgress.Progress = 0;
        EmptyBox.IsVisible = false;
        PreviewOverlay.IsVisible = false;
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

        EmptyBox.IsVisible = Episodes.Count == 0;
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

            var ext = System.IO.Path.GetExtension(photo.FileName ?? "").ToLowerInvariant();
            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg")
                ext = ".jpg";
            if (ext == ".jpeg") ext = ".jpg";

            await using var stream = await photo.OpenReadAsync();

            SetUploadUi(true, "Enviando capa...", 0.15);

            var upload = await _st.UploadCommunitySeriesCoverAsync(
                stream: stream,
                creatorUid: uid,
                seriesId: _seriesId,
                extension: ext,
                idToken: _session.IdToken
            );

            if (!upload.ok)
            {
                SetUploadUi(false, "", 0);
                await DisplayAlert("Capa", upload.message, "OK");
                return;
            }

            _series.CoverUrl = upload.url;

            SetUploadUi(true, "Salvando...", 0.75);

            var up = await _db.UpsertCommunitySeriesAsync(uid, _series, _session.IdToken);
            if (!up.ok)
            {
                SetUploadUi(false, "", 0);
                await DisplayAlert("Capa", up.message, "OK");
                return;
            }

            CoverImage.Source = upload.url;

            SetUploadUi(false, "", 0);
        }
        catch (Exception ex)
        {
            SetUploadUi(false, "", 0);
            await DisplayAlert("Capa", $"Falha ao trocar capa: {ex.Message}", "OK");
        }
    }

    private async void OnAddEpisodeClicked(object sender, EventArgs e)
    {
        if (_isUploading) return;

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

        var numStr = await DisplayPromptAsync("Novo episódio", "Número (ex: 1):", keyboard: Keyboard.Numeric);
        if (string.IsNullOrWhiteSpace(numStr)) return;

        if (!int.TryParse(numStr.Trim(), out var number) || number <= 0)
        {
            await DisplayAlert("Episódio", "Número inválido.", "OK");
            return;
        }

        var title = await DisplayPromptAsync("Novo episódio", "Título do episódio:");
        if (string.IsNullOrWhiteSpace(title)) return;

        try
        {
            FileResult? pick = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Selecione o vídeo (mp4)",
                FileTypes = FilePickerFileType.Videos
            });

            if (pick == null) return;

            var ext = System.IO.Path.GetExtension(pick.FileName ?? "").ToLowerInvariant();
            if (ext != ".mp4")
            {
                await DisplayAlert("Episódio", "Por enquanto aceitamos apenas .mp4.", "OK");
                return;
            }

            var episodeId = Guid.NewGuid().ToString("N");

            await using var videoStream = await pick.OpenReadAsync();

            SetUploadUi(true, "Enviando episódio...", 0.05);

            var upload = await _st.UploadCommunityEpisodeMp4Async(
                stream: videoStream,
                creatorUid: uid,
                seriesId: _seriesId,
                episodeId: episodeId,
                idToken: _session.IdToken
            );

            if (!upload.ok)
            {
                SetUploadUi(false, "", 0);
                await DisplayAlert("Episódio", upload.message, "OK");
                return;
            }

            SetUploadUi(true, "Salvando episódio...", 0.85);

            var ep = new CommunityEpisode
            {
                Id = episodeId,
                Number = number,
                Title = title.Trim(),
                VideoUrl = upload.url,
                DurationSeconds = 0,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            var up = await _db.UpsertCommunityEpisodeAsync(uid, _seriesId, ep, _session.IdToken);
            if (!up.ok)
            {
                SetUploadUi(false, "", 0);
                await DisplayAlert("Episódio", up.message, "OK");
                return;
            }

            SetUploadUi(false, "", 0);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            SetUploadUi(false, "", 0);
            await DisplayAlert("Episódio", $"Falha ao adicionar episódio: {ex.Message}", "OK");
        }
    }

    // Abre o player com FILA COMPLETA (todos episódios)
    private async void OnPlaySeriesClicked(object sender, EventArgs e)
    {
        try
        {
            if (_series == null)
                _series = await _db.GetCommunitySeriesAsync(_seriesId, _session.IdToken);

            var eps = await _db.GetCommunityEpisodesAsync(_seriesId, _session.IdToken);

            var feed = new List<CommunityService.EpisodeFeedItem>();

            foreach (var ep in eps.OrderBy(x => x.Number))
            {
                feed.Add(new CommunityService.EpisodeFeedItem
                {
                    SeriesId = _seriesId,
                    CreatorName = _series?.CreatorName ?? "Criador",
                    DramaTitle = _series?.Title ?? "Série",
                    DramaCoverUrl = _series?.CoverUrl ?? "",
                    EpisodeId = ep.Id,
                    EpisodeNumber = ep.Number,
                    EpisodeTitle = ep.Title ?? "",
                    VideoUrl = ep.VideoUrl ?? ""
                });
            }

            if (feed.Count == 0)
            {
                await DisplayAlert("Player", "Essa série ainda não tem episódios.", "OK");
                return;
            }

            await Navigation.PushAsync(new TikTokPlayerPage(feed, startIndex: 0));
        }
        catch (Exception ex)
        {
            await DisplayAlert("Player", $"Falha ao abrir player: {ex.Message}", "OK");
        }
    }

    // =========================
    // EDIT / REMOVE / PREVIEW
    // =========================

    private async void OnEditEpisodeClicked(object sender, EventArgs e)
    {
        if (_isUploading) return;

        if ((sender as ImageButton)?.CommandParameter is not CommunityEpisode ep)
            return;

        var uid = _session.UserId ?? "";
        if (string.IsNullOrWhiteSpace(uid))
        {
            await DisplayAlert("Conta", "Você precisa estar logado.", "OK");
            return;
        }

        var newNumStr = await DisplayPromptAsync("Editar episódio", "Número:",
            initialValue: ep.Number.ToString(),
            keyboard: Keyboard.Numeric);

        if (string.IsNullOrWhiteSpace(newNumStr)) return;

        if (!int.TryParse(newNumStr.Trim(), out var newNumber) || newNumber <= 0)
        {
            await DisplayAlert("Editar", "Número inválido.", "OK");
            return;
        }

        var newTitle = await DisplayPromptAsync("Editar episódio", "Título:", initialValue: ep.Title);
        if (string.IsNullOrWhiteSpace(newTitle)) return;

        var replaceVideo = await DisplayAlert("Editar vídeo", "Deseja substituir o vídeo (.mp4) deste episódio?", "Sim", "Não");

        string videoUrl = ep.VideoUrl ?? "";

        if (replaceVideo)
        {
            try
            {
                FileResult? pick = await FilePicker.Default.PickAsync(new PickOptions
                {
                    PickerTitle = "Selecione o NOVO vídeo (mp4)",
                    FileTypes = FilePickerFileType.Videos
                });

                if (pick == null) return;

                var ext = System.IO.Path.GetExtension(pick.FileName ?? "").ToLowerInvariant();
                if (ext != ".mp4")
                {
                    await DisplayAlert("Editar", "Por enquanto aceitamos apenas .mp4.", "OK");
                    return;
                }

                await using var stream = await pick.OpenReadAsync();

                SetUploadUi(true, "Enviando novo vídeo...", 0.10);

                var upload = await _st.UploadCommunityEpisodeMp4Async(
                    stream: stream,
                    creatorUid: uid,
                    seriesId: _seriesId,
                    episodeId: ep.Id, // substitui o arquivo no mesmo path
                    idToken: _session.IdToken
                );

                if (!upload.ok)
                {
                    SetUploadUi(false, "", 0);
                    await DisplayAlert("Editar", upload.message, "OK");
                    return;
                }

                videoUrl = upload.url;
            }
            catch (Exception ex)
            {
                SetUploadUi(false, "", 0);
                await DisplayAlert("Editar", $"Falha ao trocar vídeo: {ex.Message}", "OK");
                return;
            }
        }

        try
        {
            SetUploadUi(true, "Salvando alterações...", 0.85);

            var updated = new CommunityEpisode
            {
                Id = ep.Id,
                Number = newNumber,
                Title = newTitle.Trim(),
                VideoUrl = videoUrl,
                DurationSeconds = ep.DurationSeconds,
                CreatedAtUnix = ep.CreatedAtUnix
            };

            var up = await _db.UpsertCommunityEpisodeAsync(uid, _seriesId, updated, _session.IdToken);
            if (!up.ok)
            {
                SetUploadUi(false, "", 0);
                await DisplayAlert("Editar", up.message, "OK");
                return;
            }

            SetUploadUi(false, "", 0);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            SetUploadUi(false, "", 0);
            await DisplayAlert("Editar", $"Falha ao salvar: {ex.Message}", "OK");
        }
    }

    private async void OnRemoveEpisodeClicked(object sender, EventArgs e)
    {
        if (_isUploading) return;

        if ((sender as ImageButton)?.CommandParameter is not CommunityEpisode ep)
            return;

        var uid = _session.UserId ?? "";
        if (string.IsNullOrWhiteSpace(uid))
        {
            await DisplayAlert("Conta", "Você precisa estar logado.", "OK");
            return;
        }

        var ok = await DisplayAlert("Remover", $"Remover o episódio {ep.Number} – {ep.Title}?", "Remover", "Cancelar");
        if (!ok) return;

        try
        {
            SetUploadUi(true, "Removendo...", 0.70);

            var del = await _db.DeleteAsync($"community/series/{_seriesId}/episodes/{ep.Id}", _session.IdToken);
            if (!del.ok)
            {
                SetUploadUi(false, "", 0);
                await DisplayAlert("Remover", del.message, "OK");
                return;
            }

            await _db.PatchAsync(
                $"community/series/{_seriesId}",
                new { updatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() },
                _session.IdToken
            );

            SetUploadUi(false, "", 0);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            SetUploadUi(false, "", 0);
            await DisplayAlert("Remover", $"Falha ao remover: {ex.Message}", "OK");
        }
    }

    private void OnPreviewEpisodeClicked(object sender, EventArgs e)
    {
        if ((sender as ImageButton)?.CommandParameter is not CommunityEpisode ep)
            return;

        PreviewTitle.Text = $"Ep {ep.Number} • {ep.Title}";
        PreviewPlayer.Source = ep.VideoUrl ?? "";
        PreviewOverlay.IsVisible = true;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            try { PreviewPlayer.Play(); } catch { }
        });
    }

    private void OnClosePreviewClicked(object sender, EventArgs e)
    {
        try { PreviewPlayer.Stop(); } catch { }
        PreviewOverlay.IsVisible = false;
    }

    // =========================
    // UI helper
    // =========================
    private void SetUploadUi(bool isUploading, string text, double progress)
    {
        _isUploading = isUploading;

        UploadTopBar.IsVisible = isUploading;
        UploadText.Text = text ?? (isUploading ? "Enviando..." : "");
        UploadProgress.Progress = Math.Clamp(progress, 0, 1);
    }
}