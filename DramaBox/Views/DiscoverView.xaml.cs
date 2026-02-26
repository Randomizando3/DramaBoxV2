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

    // ? usado pelo XAML do Hero (Carousel)
    public ObservableCollection<HeroItem> FeaturedItems { get; } = new();

    private string _selectedCategory = "Novidades";

    public DiscoverView()
    {
        InitializeComponent();

        _session = Resolve<SessionService>() ?? new SessionService();
        _db = Resolve<FirebaseDatabaseService>() ?? new FirebaseDatabaseService(new HttpClient());

        // Top + Feed continuam usando as mesmas fontes
        TopRow.ItemsSource = _top10;
        FeedList.ItemsSource = _feed;

        // CategoryRow ficou invisível, mas mantive compatibilidade
        CategoryRow.ItemsSource = _categories;

        EnsureDefaultCategories();

        // BindingContext por último (pra garantir que FeaturedItems existe)
        BindingContext = this;
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
            var list = await _db.GetAllDramasAsync(_session.IdToken);

            _all.Clear();
            foreach (var it in list)
                _all.Add(it);

            // Featured source
            _featured.Clear();
            foreach (var f in _all.Where(x => x.IsFeatured).Take(12))
                _featured.Add(f);

            // ? monta HeroItems (texto igual do print)
            FeaturedItems.Clear();
            foreach (var f in _featured.Take(8))
            {
                FeaturedItems.Add(new HeroItem
                {
                    Drama = f,
                    Title = f.Title ?? "Drama",
                    EpisodesText = BuildEpisodesText(f),
                    DurationText = BuildDurationText(f),
                    AgeText = BuildAgeText(f),
                    CoverUrl = f.CoverUrl ?? ""
                });
            }

            // Top 10
            _top10.Clear();
            foreach (var t in _all
                         .OrderBy(x => x.TopRank == 0 ? int.MaxValue : x.TopRank)
                         .Take(10))
                _top10.Add(t);

            // Atualiza feed
            ApplyFilters(SearchEntry?.Text);
        }
        catch
        {
            _featured.Clear();
            FeaturedItems.Clear();
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
    // EVENTS (mantidos)
    // =========================

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        => ApplyFilters(e.NewTextValue);

    private async void OnVipClicked(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("upgrade");

    // Mantido (mesmo que CategoryRow esteja oculto por enquanto)
    private void OnCategorySelected(object sender, SelectionChangedEventArgs e)
    {
        var cat = e.CurrentSelection?.FirstOrDefault() as string;
        if (string.IsNullOrWhiteSpace(cat))
            return;

        _selectedCategory = cat;
        ApplyFilters(SearchEntry?.Text);
        CategoryRow.SelectedItem = null;
    }

    // ? chips agora são botões (Drama/Suspense/Fantasia)
    private void OnChipClicked(object sender, EventArgs e)
    {
        if (sender is Button b && !string.IsNullOrWhiteSpace(b.Text))
        {
            _selectedCategory = b.Text.Trim();
            ApplyFilters(SearchEntry?.Text);
        }
    }

    // ? botão do presente (por enquanto só placeholder)
    private async void OnRewardsClicked(object sender, EventArgs e)
        => await Navigation.PushAsync(new RewardsView());

    private async Task OpenDramaAsync(DramaSeries? drama)
    {
        if (drama == null) return;

        var id = drama.Id ?? "";
        if (string.IsNullOrWhiteSpace(id)) return;

        await Navigation.PushAsync(new DramaDetailsPage(id));
    }

    // Mantido (não usado no Hero atual, mas não removi)
    private async void OnFeaturedSelected(object sender, SelectionChangedEventArgs e)
    {
        var drama = e.CurrentSelection?.FirstOrDefault() as DramaSeries;
        if (sender is CollectionView cv) cv.SelectedItem = null;
        await OpenDramaAsync(drama);
    }

    // ? novo handler do Carousel do Hero
    private void OnFeaturedCurrentItemChanged(object sender, CurrentItemChangedEventArgs e)
    {
        // não abre ao trocar (igual TikTok, só navega por swipe)
        // se quiser abrir ao tocar: use OnHeroPlayClicked
    }

    // ? “Assistir” no Hero
    private async void OnHeroPlayClicked(object sender, EventArgs e)
    {
        var hero = FeaturedCarousel?.CurrentItem as HeroItem;
        if (hero?.Drama == null) return;
        await OpenDramaAsync(hero.Drama);
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
        => await Navigation.PushAsync(new SeriesListPage("Em destaque", "__TOP__", _all.ToList()));

    // =========================
    // Helpers do HERO (textos)
    // =========================

    private static string BuildEpisodesText(DramaSeries s)
    {
        // Se você tiver EpisodesCount no model, troca aqui.
        // Fallback: "— episódios"
        return "— episódios";
    }

    private static string BuildDurationText(DramaSeries s)
    {
        // Se tiver duração no model, troca aqui.
        return "2–5 min";
    }

    private static string BuildAgeText(DramaSeries s)
    {
        // Se tiver age rating no model, troca aqui.
        return "12+";
    }

    // DTO do Hero (pra ficar igual ao print)
    public sealed class HeroItem
    {
        public DramaSeries? Drama { get; set; }
        public string Title { get; set; } = "";
        public string CoverUrl { get; set; } = "";

        public string EpisodesText { get; set; } = "";
        public string DurationText { get; set; } = "";
        public string AgeText { get; set; } = "";
    }
}
