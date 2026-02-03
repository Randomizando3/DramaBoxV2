using System.Collections.ObjectModel;
using DramaBox.Models;
using DramaBox.Services;

namespace DramaBox.Views;

public partial class DramaDetailsPage : ContentPage
{
    private readonly FirebaseDatabaseService _db;
    private readonly SessionService _session;

    private readonly string _dramaId;
    public ObservableCollection<DramaEpisode> Episodes { get; } = new();

    public DramaDetailsPage(string dramaId)
    {
        InitializeComponent();

        _db = Resolve<FirebaseDatabaseService>() ?? new FirebaseDatabaseService(new HttpClient());
        _session = Resolve<SessionService>() ?? new SessionService();

        _dramaId = dramaId;
        EpisodesList.ItemsSource = Episodes;
    }

    private static T? Resolve<T>() where T : class
        => Application.Current?.Handler?.MauiContext?.Services?.GetService(typeof(T)) as T;

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        var drama = await _db.GetDramaAsync(_dramaId, _session.IdToken);
        TitleLabel.Text = drama?.Title ?? "Detalhes";
        CoverImage.Source = drama?.PosterUrl ?? drama?.CoverUrl ?? "";

        var eps = await _db.GetEpisodesAsync(_dramaId, _session.IdToken);
        Episodes.Clear();
        foreach (var ep in eps)
            Episodes.Add(ep);
    }

    private async void OnBackClicked(object sender, EventArgs e)
        => await Navigation.PopAsync();

    private async void OnEpisodeSelected(object sender, SelectionChangedEventArgs e)
    {
        var ep = e.CurrentSelection?.FirstOrDefault() as DramaEpisode;
        ((CollectionView)sender).SelectedItem = null;
        if (ep == null) return;

        await Navigation.PushAsync(new PlayerPage(ep.Title, ep.VideoUrl));
    }
}
