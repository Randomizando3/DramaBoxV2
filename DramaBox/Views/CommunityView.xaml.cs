using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using DramaBox.Services;
using Microsoft.Maui.Controls;

namespace DramaBox.Views;

public partial class CommunityView : ContentPage
{
    private readonly SessionService _session;
    private readonly FirebaseDatabaseService _db;

    private string _mode = "recommended";

    public ObservableCollection<CommunityFeedRow> FeedItems { get; } = new();

    public CommunityView()
    {
        InitializeComponent();

        _session = Resolve<SessionService>() ?? new SessionService();
        _db = Resolve<FirebaseDatabaseService>() ?? new FirebaseDatabaseService(new HttpClient());

        BindingContext = this;
        ApplyTabVisual();
    }

    private static T? Resolve<T>() where T : class
        => Application.Current?.Handler?.MauiContext?.Services?.GetService(typeof(T)) as T;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadFeedAsync();
    }

    private void ApplyTabVisual()
    {
        var primary = Application.Current?.Resources.TryGetValue("AppPrimaryColor", out var p) == true ? (Color)p : Colors.Black;
        var muted = Application.Current?.Resources.TryGetValue("AppMutedTextColor", out var m) == true ? (Color)m : Colors.Gray;

        StyleBtn(TabRecommended, _mode == "recommended");
        StyleBtn(TabPopular, _mode == "popular");
        StyleBtn(TabCommunity, _mode == "community");

        void StyleBtn(Button b, bool active)
        {
            b.BackgroundColor = active ? primary : Colors.Transparent;
            b.TextColor = active ? Colors.White : muted;
        }
    }

    private async Task LoadFeedAsync()
    {
        try
        {
            var uid = _session.UserId ?? "";
            var token = _session.IdToken;

            var list = await _db.GetCommunityFeedAsync(_mode, take: 30, token);

            FeedItems.Clear();

            foreach (var s in list)
            {
                var cover = string.IsNullOrWhiteSpace(s.CoverUrl) ? (s.PosterUrl ?? "") : s.CoverUrl;

                // Like é na SÉRIE
                var liked = false;
                if (!string.IsNullOrWhiteSpace(uid) && !string.IsNullOrWhiteSpace(s.Id))
                    liked = (await _db.GetAsync<bool?>($"community/interactions/likes/{uid}/{s.Id}", token)) == true;

                FeedItems.Add(new CommunityFeedRow
                {
                    SeriesId = s.Id,
                    Title = s.Title ?? "",
                    Subtitle = string.IsNullOrWhiteSpace(s.CreatorName) ? "Criador" : s.CreatorName,
                    CoverUrl = cover,
                    CreatorName = string.IsNullOrWhiteSpace(s.CreatorName) ? "Criador" : s.CreatorName,
                    CreatorUserId = s.CreatorUserId ?? "",
                    IsLiked = liked,
                    Kicker = _mode switch
                    {
                        "popular" => "Autorais • Populares",
                        "community" => "Autorais • Comunidade",
                        _ => "Autorais • Destaque"
                    }
                });
            }
        }
        catch
        {
            FeedItems.Clear();
        }
    }

    private async void OnTabRecommended(object sender, EventArgs e)
    {
        _mode = "recommended";
        ApplyTabVisual();
        await LoadFeedAsync();
    }

    private async void OnTabPopular(object sender, EventArgs e)
    {
        _mode = "popular";
        ApplyTabVisual();
        await LoadFeedAsync();
    }

    private async void OnTabCommunity(object sender, EventArgs e)
    {
        _mode = "community";
        ApplyTabVisual();
        await LoadFeedAsync();
    }

    // ? Random abre fila aleatória (TikTok)
    private async void OnRandomClicked(object sender, EventArgs e)
        => await Navigation.PushAsync(new TikTokPlayerPage(true));

    // =========================
    // AÇÕES DO CARD
    // =========================

    private CommunityFeedRow? RowFromSender(object sender)
        => (sender as BindableObject)?.BindingContext as CommunityFeedRow;

    private async Task PlayRowAsync(CommunityFeedRow? row)
    {
        if (row == null) return;
        if (string.IsNullOrWhiteSpace(row.SeriesId)) return;

        // ? Série => fila de episódios da própria série (swipe up = próximo ep)
        await Navigation.PushAsync(new TikTokPlayerPage(seriesId: row.SeriesId, startEpisodeId: ""));
    }

    private async void OnPlaySeriesClicked(object sender, EventArgs e)
    {
        var row = RowFromSender(sender);
        await PlayRowAsync(row);
    }

    private async void OnCardTapped(object sender, TappedEventArgs e)
    {
        var row = (sender as BindableObject)?.BindingContext as CommunityFeedRow;
        await PlayRowAsync(row);
    }

    // Tap no Like (Grid do XAML)
    private async void OnLikeSeriesTapped(object sender, EventArgs e)
    {
        var row = RowFromSender(sender);
        if (row == null) return;

        var uid = _session.UserId ?? "";
        if (string.IsNullOrWhiteSpace(uid))
        {
            await DisplayAlert("Conta", "Você precisa estar logado para curtir.", "OK");
            return;
        }

        if (string.IsNullOrWhiteSpace(row.SeriesId) || string.IsNullOrWhiteSpace(row.CreatorUserId))
            return;

        var (ok, msg, nowLiked) = await _db.ToggleCommunityLikeAsync(
            userId: uid,
            seriesId: row.SeriesId,
            creatorUid: row.CreatorUserId,
            idToken: _session.IdToken
        );

        if (!ok)
        {
            await DisplayAlert("Curtir", msg, "OK");
            return;
        }

        row.IsLiked = nowLiked;
    }

    // Tap no Share (Grid do XAML)
    private async void OnShareSeriesTapped(object sender, EventArgs e)
    {
        var row = RowFromSender(sender);
        if (row == null) return;

        if (string.IsNullOrWhiteSpace(row.SeriesId) || string.IsNullOrWhiteSpace(row.CreatorUserId))
            return;

        var (ok, msg) = await _db.AddCommunityShareAsync(
            seriesId: row.SeriesId,
            creatorUid: row.CreatorUserId,
            idToken: _session.IdToken
        );

        if (!ok)
        {
            await DisplayAlert("Share", msg, "OK");
            return;
        }

        await DisplayAlert("Share", "Link copiado/compartilhado (MVP).", "OK");
    }

    // =========================
    // ROW
    // =========================

    public sealed class CommunityFeedRow : BindableObject
    {
        public string SeriesId { get; set; } = "";
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string CoverUrl { get; set; } = "";
        public string CreatorName { get; set; } = "";
        public string CreatorUserId { get; set; } = "";
        public string Kicker { get; set; } = "Autorais • Destaque";

        private bool _isLiked;
        public bool IsLiked
        {
            get => _isLiked;
            set
            {
                if (_isLiked == value) return;
                _isLiked = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LikeButtonText));
            }
        }

        public string LikeButtonText => IsLiked ? "Curtido" : "Curtir";
    }
}