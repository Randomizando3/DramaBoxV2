using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using DramaBox.Models;
using DramaBox.Services;

namespace DramaBox.Views;

public partial class CreationView : ContentPage
{
    private readonly SessionService _session;
    private readonly FirebaseDatabaseService _db;

    public ObservableCollection<MySeriesRow> MySeries { get; } = new();

    public CreationView()
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
        var uid = _session.UserId ?? "";
        if (string.IsNullOrWhiteSpace(uid))
            return;

        var series = await _db.GetCreatorCommunitySeriesAsync(uid, _session.IdToken);

        MySeries.Clear();

        double totalLikes = 0, totalShares = 0;

        foreach (var s in series)
        {
            var eps = await _db.GetCommunityEpisodesAsync(s.Id, _session.IdToken);

            MySeries.Add(new MySeriesRow
            {
                SeriesId = s.Id,
                Title = s.Title ?? "",
                Subtitle = s.Subtitle ?? "",
                CoverUrl = string.IsNullOrWhiteSpace(s.CoverUrl) ? (s.PosterUrl ?? "") : s.CoverUrl,
                EpisodesText = $"{eps.Count} eps"
            });
        }

        var cents = await _db.GetAsync<double?>($"community/earnings/creators/{uid}/centsTotal", _session.IdToken) ?? 0.0;
        RevenueLabel.Text = ToBRLFromCents(cents);

        foreach (var row in MySeries)
        {
            var m = await _db.GetAsync<CommunitySeriesMetrics>($"community/metrics/series/{row.SeriesId}", _session.IdToken)
                    ?? new CommunitySeriesMetrics();

            totalLikes += m.Likes;
            totalShares += m.Shares;
        }

        LikesLabel.Text = ((long)Math.Round(totalLikes)).ToString("N0", CultureInfo.GetCultureInfo("pt-BR"));
        SharesLabel.Text = ((long)Math.Round(totalShares)).ToString("N0", CultureInfo.GetCultureInfo("pt-BR"));
    }

    private static string ToBRLFromCents(double cents)
    {
        var br = CultureInfo.GetCultureInfo("pt-BR");
        var reais = cents / 100.0;
        return reais.ToString("C", br);
    }

    private async void OnRefreshClicked(object sender, EventArgs e)
        => await LoadAsync();

    private async void OnCreateSeriesClicked(object sender, EventArgs e)
    {
        var uid = _session.UserId ?? "";
        if (string.IsNullOrWhiteSpace(uid))
        {
            await DisplayAlert("Conta", "Você precisa estar logado.", "OK");
            return;
        }

        var title = await DisplayPromptAsync("Nova série", "Título da série:");
        if (string.IsNullOrWhiteSpace(title)) return;

        var subtitle = await DisplayPromptAsync("Nova série", "Subtítulo (opcional):") ?? "";

        var series = new CommunitySeries
        {
            Id = Guid.NewGuid().ToString("N"),
            CreatorUserId = uid,
            CreatorName = _session.Profile?.Nome ?? _session.Email ?? "Criador",
            CreatorIsVip = string.Equals(_session.Profile?.Plano, "premium", StringComparison.OrdinalIgnoreCase),
            Title = title.Trim(),
            Subtitle = subtitle.Trim(),
            CoverUrl = "",
            PosterUrl = "",
            IsPublished = true
        };

        var (ok, msg) = await _db.UpsertCommunitySeriesAsync(uid, series, _session.IdToken);
        if (!ok)
        {
            await DisplayAlert("Criador", msg, "OK");
            return;
        }

        // ? abre direto o editor para adicionar capa/episódios
        await Navigation.PushAsync(new CreatorSeriesEditorPage(series.Id));
    }

    private async void OnSeriesSelected(object sender, SelectionChangedEventArgs e)
    {
        var row = e.CurrentSelection?.FirstOrDefault() as MySeriesRow;
        ((CollectionView)sender).SelectedItem = null;
        if (row == null) return;

        await Navigation.PushAsync(new CreatorSeriesEditorPage(row.SeriesId));
    }

    public sealed class MySeriesRow
    {
        public string SeriesId { get; set; } = "";
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string CoverUrl { get; set; } = "";
        public string EpisodesText { get; set; } = "";
    }
}
