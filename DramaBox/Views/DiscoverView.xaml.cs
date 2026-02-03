using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using DramaBox.Models;
using DramaBox.Services;

namespace DramaBox.Views;

public partial class DiscoverView : ContentPage
{
    private readonly SessionService _session;
    private readonly FirebaseDatabaseService _db;

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

        FeaturedCarousel.ItemsSource = _featured;
        TopRow.ItemsSource = _top10;
        FeedList.ItemsSource = _feed;
        CategoryRow.ItemsSource = _categories;

        EnsureDefaultCategories();
    }

    private static T? Resolve<T>() where T : class
        => Application.Current?.Handler?.MauiContext?.Services?.GetService(typeof(T)) as T;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private void EnsureDefaultCategories()
    {
        _categories.Clear();
        _categories.Add("Novidades");
        _categories.Add("Destaques");
        _categories.Add("Romântico");
        _categories.Add("Ação");
    }

    private async Task LoadAsync()
    {
        try
        {
            // ? Busca em /catalog/dramas
            var list = await _db.GetAllDramasAsync(_session.IdToken);

            _all.Clear();
            foreach (var it in list)
                _all.Add(it);

            // Featured
            _featured.Clear();
            foreach (var f in _all.Where(x => x.IsFeatured).Take(12))
                _featured.Add(f);

            // Top 10
            _top10.Clear();
            foreach (var t in _all.OrderBy(x => x.TopRank == 0 ? int.MaxValue : x.TopRank).Take(10))
                _top10.Add(t);

            // Atualiza feed
            ApplyFilters(SearchEntry?.Text);
        }
        catch
        {
            _featured.Clear();
            _top10.Clear();
            _feed.Clear();
        }
    }

    private void ApplyFilters(string? query)
    {
        query ??= "";
        query = query.Trim();

        FeedTitle.Text = string.IsNullOrWhiteSpace(_selectedCategory) ? "Novidades" : _selectedCategory;

        var baseSet = _all.AsEnumerable();

        // categoria
        if (!string.IsNullOrWhiteSpace(_selectedCategory))
        {
            baseSet = baseSet.Where(d =>
                d.Categories != null &&
                d.Categories.Any(c => string.Equals(c, _selectedCategory, StringComparison.OrdinalIgnoreCase)));
        }

        // busca
        if (!string.IsNullOrWhiteSpace(query))
        {
            baseSet = baseSet.Where(d =>
                (d.Title ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (d.Subtitle ?? "").Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        var list = baseSet
            .OrderByDescending(x => x.UpdatedAtUnix)
            .ThenBy(x => x.TopRank == 0 ? int.MaxValue : x.TopRank)
            .Take(30)
            .ToList();

        _feed.Clear();
        foreach (var it in list)
            _feed.Add(it);
    }

    // =========================
    // EVENTS
    // =========================

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        => ApplyFilters(e.NewTextValue);

    private async void OnVipClicked(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("upgrade");

    private void OnCategorySelected(object sender, SelectionChangedEventArgs e)
    {
        var cat = e.CurrentSelection?.FirstOrDefault() as string;
        if (string.IsNullOrWhiteSpace(cat))
            return;

        _selectedCategory = cat;
        ApplyFilters(SearchEntry?.Text);

        // limpa seleção pra poder clicar de novo no mesmo chip
        CategoryRow.SelectedItem = null;
    }

    private async Task OpenDramaAsync(DramaSeries? drama)
    {
        if (drama == null) return;

        var id = drama.Id ?? "";
        if (string.IsNullOrWhiteSpace(id)) return;

        // sua tela de detalhes está esperando ID (padrão que você já está usando)
        await Navigation.PushAsync(new DramaDetailsPage(id));
    }

    private async void OnFeaturedSelected(object sender, SelectionChangedEventArgs e)
    {
        var drama = e.CurrentSelection?.FirstOrDefault() as DramaSeries;
        FeaturedCarousel.SelectedItem = null;
        await OpenDramaAsync(drama);
    }

    private async void OnTopSelected(object sender, SelectionChangedEventArgs e)
    {
        var drama = e.CurrentSelection?.FirstOrDefault() as DramaSeries;
        TopRow.SelectedItem = null;
        await OpenDramaAsync(drama);
    }

    private async void OnFeedSelected(object sender, SelectionChangedEventArgs e)
    {
        var drama = e.CurrentSelection?.FirstOrDefault() as DramaSeries;
        FeedList.SelectedItem = null;
        await OpenDramaAsync(drama);
    }

    private async void OnSeeAllCategoryClicked(object sender, EventArgs e)
        => await Navigation.PushAsync(new SeriesListPage(_selectedCategory, _selectedCategory, _all.ToList()));

    private async void OnSeeAllTopClicked(object sender, EventArgs e)
        => await Navigation.PushAsync(new SeriesListPage("Top Séries", "__TOP__", _all.ToList()));
}
