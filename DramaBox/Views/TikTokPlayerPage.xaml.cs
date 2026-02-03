using CommunityToolkit.Maui.Core.Primitives;
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

    private readonly string _mode;
    private readonly string _seriesId;

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

    // =========================================================
    // 1) Construtor NOVO: recebe feed pronto + startIndex
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

        BindingContext = this;

        Items.Clear();
        if (feed != null)
        {
            foreach (var x in feed)
            {
                Items.Add(new EpisodeItem
                {
                    SeriesId = x.SeriesId ?? "",
                    CreatorUserId = "", // opcional (se quiser royalties 100%, preencha no feed)
                    CreatorName = x.CreatorName ?? "Criador",
                    DramaTitle = x.DramaTitle ?? "",
                    DramaCoverUrl = x.DramaCoverUrl ?? "",
                    EpisodeId = x.EpisodeId ?? "",
                    EpisodeNumber = x.EpisodeNumber,
                    EpisodeTitle = x.EpisodeTitle ?? "",
                    VideoUrl = x.VideoUrl ?? ""
                });
            }
        }

        Feed.ItemsSource = Items;

        if (startIndex < 0) startIndex = 0;
        if (startIndex >= Items.Count) startIndex = 0;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (Items.Count > 0)
            {
                Feed.Position = startIndex;
                Current = Items[startIndex];
                PlayCurrent();
            }
        });
    }

    // =========================================================
    // 2) Construtor ANTIGO: mode/seriesId
    // =========================================================
    public TikTokPlayerPage(string mode = "random", string seriesId = "")
    {
        InitializeComponent();

        _session = Resolve<SessionService>() ?? new SessionService();
        _db = Resolve<FirebaseDatabaseService>() ?? new FirebaseDatabaseService(new HttpClient());
        var st = Resolve<FirebaseStorageService>() ?? new FirebaseStorageService(new HttpClient());
        _community = Resolve<CommunityService>() ?? new CommunityService(_db, st, _session);

        _mode = (mode ?? "random").Trim().ToLowerInvariant();
        _seriesId = seriesId ?? "";

        BindingContext = this;
        Feed.ItemsSource = Items;
    }

    private static T? Resolve<T>() where T : class
        => Application.Current?.Handler?.MauiContext?.Services?.GetService(typeof(T)) as T;

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_mode != "feed")
        {
            await LoadAsync();

            if (Items.Count > 0)
            {
                Current = Items[0];
                Feed.Position = 0;
                PlayCurrent();
            }
        }
    }

    protected override void OnDisappearing()
    {
        try { Player?.Stop(); } catch { }
        base.OnDisappearing();
    }

    private async Task LoadAsync()
    {
        Items.Clear();

        try
        {
            if (_mode == "series" && !string.IsNullOrWhiteSpace(_seriesId))
            {
                // Se você tiver métodos do CommunityService, use eles.
                // Aqui mantive o que você já estava fazendo no seu antigo código:
                var s = await _db.GetCommunitySeriesAsync(_seriesId, _session.IdToken);
                var eps = await _db.GetCommunityEpisodesAsync(_seriesId, _session.IdToken);

                foreach (var ep in eps.OrderBy(x => x.Number))
                {
                    Items.Add(new EpisodeItem
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
                    });
                }
            }
            else
            {
                var list = await _db.GetCommunityFeedAsync("random", take: 25, _session.IdToken);

                foreach (var s in list)
                {
                    var eps = await _db.GetCommunityEpisodesAsync(s.Id, _session.IdToken);

                    foreach (var ep in eps.OrderBy(x => x.Number))
                    {
                        Items.Add(new EpisodeItem
                        {
                            SeriesId = s.Id,
                            CreatorUserId = s.CreatorUserId ?? "",
                            CreatorName = s.CreatorName ?? "Criador",
                            DramaTitle = s.Title ?? "Série",
                            DramaCoverUrl = s.CoverUrl ?? "",
                            EpisodeId = ep.Id,
                            EpisodeNumber = ep.Number,
                            EpisodeTitle = ep.Title ?? "",
                            VideoUrl = ep.VideoUrl ?? ""
                        });
                    }
                }
            }
        }
        catch
        {
            Items.Clear();
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
        }
        catch
        {
            // não quebra UI se o player falhar
        }
    }

    private void OnCurrentItemChanged(object sender, CurrentItemChangedEventArgs e)
    {
        Current = e.CurrentItem as EpisodeItem;
        PlayCurrent();
    }

    private void OnScreenTapped(object sender, TappedEventArgs e)
    {
        try
        {
            // CurrentState existe no toolkit MediaElement
            if (Player.CurrentState == MediaElementState.Playing)
                Player.Pause();
            else
                Player.Play();
        }
        catch { }
    }

    private async void OnBackClicked(object sender, EventArgs e)
        => await Navigation.PopAsync();

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
