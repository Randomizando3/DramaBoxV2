using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using DramaBox.Models;
using DramaBox.Services;

namespace DramaBox.Views;

public partial class DiscoverView : ContentPage
{
    private readonly SessionService _session;
    private readonly FirebaseDatabaseService _db;

    // dados
    private readonly ObservableCollection<DramaSeries> _all = new();
    private readonly ObservableCollection<DramaSeries> _featured = new();
    private readonly ObservableCollection<DramaSeries> _top10 = new();
    private readonly ObservableCollection<DramaSeries> _feed = new();
    private readonly ObservableCollection<string> _categories = new();

    private string _selectedCategory = "Novidades";

    public DiscoverView()
    {
        InitializeComponent();

        _session = Resolve<SessionService>() ?? new SessionService();
        _db = Resolve<FirebaseDatabaseService>() ?? new FirebaseDatabaseService(new HttpClient());

        // binds diretos
        FeaturedCarousel.ItemsSource = _featured;
        TopRow.ItemsSource = _top10;
        FeedList.ItemsSource = _feed;
        CategoryRow.ItemsSource = _categories;

        FeedTitleLabel.Text = _selectedCategory;
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
            var list = await _db.GetAllDramasAsync(_session.IdToken);

            _all.Clear();
            foreach (var it in list)
                _all.Add(it);

            // featured
            var featured = _all
                .Where(x => x.IsFeatured)
                .OrderBy(x => x.TopRank == 0 ? int.MaxValue : x.TopRank)
                .ThenByDescending(x => x.UpdatedAtUnix)
                .ToList();

            _featured.Clear();
            foreach (var f in featured)
                _featured.Add(f);

            // top 10
            var top = _all
                .OrderBy(x => x.TopRank == 0 ? int.MaxValue : x.TopRank)
                .ThenByDescending(x => x.UpdatedAtUnix)
                .Take(10)
                .ToList();

            _top10.Clear();
            foreach (var t in top)
                _top10.Add(t);

            // categorias (fixas + dinâmicas do json)
            var dynCats = _all
                .Where(x => x.Categories != null)
                .SelectMany(x => x.Categories!)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c)
                .ToList();

            _categories.Clear();

            // “principais” primeiro
            AddCatIfMissing("Novidades");
            AddCatIfMissing("Destaques");
            AddCatIfMissing("Romântico");
            AddCatIfMissing("Ação");

            foreach (var c in dynCats)
                AddCatIfMissing(c);

            // feed inicial
            BuildFeed();
        }
        catch
        {
            _featured.Clear();
            _top10.Clear();
            _feed.Clear();
            _categories.Clear();
        }
    }

    private void AddCatIfMissing(string cat)
    {
        if (string.IsNullOrWhiteSpace(cat)) return;

        if (!_categories.Any(x => string.Equals(x, cat, StringComparison.OrdinalIgnoreCase)))
            _categories.Add(cat);
    }

    private void BuildFeed()
    {
        var query = (SearchEntry?.Text ?? "").Trim();
        var category = (_selectedCategory ?? "").Trim();

        FeedTitleLabel.Text = string.IsNullOrWhiteSpace(category) ? "Novidades" : category;

        var src = _all.AsEnumerable();

        // filtro categoria
        if (!string.IsNullOrWhiteSpace(category))
        {
            src = src.Where(d =>
                d.Categories != null &&
                d.Categories.Any(c => string.Equals(c, category, StringComparison.OrdinalIgnoreCase)));
        }

        // filtro busca
        if (!string.IsNullOrWhiteSpace(query))
        {
            src = src.Where(d =>
                (d.Title ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (d.Subtitle ?? "").Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        var list = src
            .OrderByDescending(x => x.UpdatedAtUnix)
            .ThenBy(x => x.TopRank == 0 ? int.MaxValue : x.TopRank)
            .ToList();

        _feed.Clear();
        foreach (var it in list)
            _feed.Add(it);
    }

    // =========================
    // EVENTS (ASSINATURAS CERTAS)
    // =========================

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
        => BuildFeed();

    private async void OnVipClicked(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("upgrade");

    private async void OnFeaturedSelected(object sender, SelectionChangedEventArgs e)
    {
        var drama = e.CurrentSelection?.FirstOrDefault() as DramaSeries;
        ((CollectionView)sender).SelectedItem = null;
        if (drama == null) return;

        await OpenDramaAsync(drama);
    }

    private void OnCategorySelected(object sender, SelectionChangedEventArgs e)
    {
        var cat = e.CurrentSelection?.FirstOrDefault() as string;
        ((CollectionView)sender).SelectedItem = null;

        if (string.IsNullOrWhiteSpace(cat))
            return;

        _selectedCategory = cat;
        BuildFeed();
    }

    private async void OnTopSelected(object sender, SelectionChangedEventArgs e)
    {
        var drama = e.CurrentSelection?.FirstOrDefault() as DramaSeries;
        ((CollectionView)sender).SelectedItem = null;
        if (drama == null) return;

        await OpenDramaAsync(drama);
    }

    private async void OnFeedSelected(object sender, SelectionChangedEventArgs e)
    {
        var drama = e.CurrentSelection?.FirstOrDefault() as DramaSeries;
        ((CollectionView)sender).SelectedItem = null;
        if (drama == null) return;

        await OpenDramaAsync(drama);
    }

    private async Task OpenDramaAsync(DramaSeries drama)
    {
        // ? Escolha 1 (SE sua página recebe DramaSeries)
        // await Navigation.PushAsync(new DramaDetailsPage(drama));

        // ? Escolha 2 (SE sua página recebe string dramaId)
        var id = drama.Id ?? "";
        if (string.IsNullOrWhiteSpace(id)) return;

        await Navigation.PushAsync(new DramaDetailsPage(id));
    }

    private async void OnSeeAllTopClicked(object sender, EventArgs e)
    {
        // “Top” = lista completa ordenada por rank (ou topRank)
        var all = _all
            .OrderBy(x => x.TopRank == 0 ? int.MaxValue : x.TopRank)
            .ThenByDescending(x => x.UpdatedAtUnix)
            .ToList();

        await Navigation.PushAsync(new SeriesListPage("Top Séries", "TOP", all));
    }

    private async void OnSeeAllFeedClicked(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new SeriesListPage(_selectedCategory, _selectedCategory, _all.ToList()));
    }
}
