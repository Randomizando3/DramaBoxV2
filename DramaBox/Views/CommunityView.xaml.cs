using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using DramaBox.Models;
using DramaBox.Services;

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
        // ? evita crash caso ainda não exista (mas você já vai criar no App.xaml)
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
            var list = await _db.GetCommunityFeedAsync(_mode, take: 30, _session.IdToken);

            FeedItems.Clear();
            foreach (var s in list)
            {
                FeedItems.Add(new CommunityFeedRow
                {
                    SeriesId = s.Id,
                    Title = s.Title ?? "",
                    CoverUrl = string.IsNullOrWhiteSpace(s.CoverUrl) ? (s.PosterUrl ?? "") : s.CoverUrl,
                    CreatorName = string.IsNullOrWhiteSpace(s.CreatorName) ? "Criador" : s.CreatorName,
                    CreatorUserId = s.CreatorUserId ?? "",
                    BadgesText = "Toque para assistir"
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

    private async void OnRandomClicked(object sender, EventArgs e)
    {
        // abre player com lista aleatória (episódios de várias séries)
        await Navigation.PushAsync(new TikTokPlayerPage(mode: "random"));
    }

    private async void OnPlaySeriesClicked(object sender, EventArgs e)
    {
        if (sender is not BindableObject bo || bo.BindingContext is not CommunityFeedRow row)
            return;

        // abre o player "tiktok" já focado na série clicada
        await Navigation.PushAsync(new TikTokPlayerPage(mode: "series", seriesId: row.SeriesId));
    }

    public sealed class CommunityFeedRow
    {
        public string SeriesId { get; set; } = "";
        public string Title { get; set; } = "";
        public string CoverUrl { get; set; } = "";
        public string CreatorName { get; set; } = "";
        public string CreatorUserId { get; set; } = "";
        public string BadgesText { get; set; } = "";
    }
}
