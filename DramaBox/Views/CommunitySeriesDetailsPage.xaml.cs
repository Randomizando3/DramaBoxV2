using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using DramaBox.Services;

namespace DramaBox.Views;

public partial class CommunitySeriesDetailsPage : ContentPage
{
    private readonly SessionService _session;
    private readonly CommunityService _community;

    private readonly string _seriesId;

    private CommunityService.CommunitySeriesDto? _series;
    public ObservableCollection<CommunityService.CommunityEpisodeDto> Episodes { get; } = new();

    public CommunitySeriesDetailsPage(string seriesId)
    {
        InitializeComponent();

        _seriesId = seriesId ?? "";

        _session = Resolve<SessionService>() ?? new SessionService();
        var db = Resolve<FirebaseDatabaseService>() ?? new FirebaseDatabaseService(new HttpClient());
        var st = Resolve<FirebaseStorageService>() ?? new FirebaseStorageService(new HttpClient());

        _community = new CommunityService(db, st, _session);

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

        _series = await _community.GetSeriesAsync(_seriesId);
        TitleLabel.Text = _series?.Title ?? "Série";
        SubtitleLabel.Text = _series?.Subtitle ?? "";
        CreatorLabel.Text = string.IsNullOrWhiteSpace(_series?.CreatorName) ? "por —" : $"por {_series!.CreatorName}";
        CoverImage.Source = _series?.CoverUrl ?? "";

        var eps = await _community.GetEpisodesAsync(_seriesId);
        Episodes.Clear();
        foreach (var ep in eps.OrderBy(x => x.Number))
            Episodes.Add(ep);
    }

    private async void OnBackClicked(object sender, EventArgs e)
        => await Navigation.PopAsync();

    private async void OnPlayFirstClicked(object sender, EventArgs e)
    {
        if (Episodes.Count == 0 || _series == null)
            return;

        var feed = await _community.BuildEpisodeFeedFromSeriesAsync(_seriesId);
        await Navigation.PushAsync(new TikTokPlayerPage(feed, 0));
    }

    private async void OnEpisodeSelected(object sender, SelectionChangedEventArgs e)
    {
        var ep = e.CurrentSelection?.FirstOrDefault() as CommunityService.CommunityEpisodeDto;
        ((CollectionView)sender).SelectedItem = null;
        if (ep == null || _series == null) return;

        var feed = await _community.BuildEpisodeFeedFromSeriesAsync(_seriesId);
        var idx = feed.FindIndex(x => x.EpisodeId == ep.EpisodeId);
        if (idx < 0) idx = 0;

        await Navigation.PushAsync(new TikTokPlayerPage(feed, idx));
    }

}
