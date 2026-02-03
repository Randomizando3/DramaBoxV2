using System;
using System.Collections.ObjectModel;
using System.Linq;
using DramaBox.Models;

namespace DramaBox.Views;

public partial class SeriesListPage : ContentPage
{
    private readonly List<DramaSeries> _all;
    private readonly string _category;

    private readonly ObservableCollection<DramaSeries> _items = new();

    public SeriesListPage(string title, string category, List<DramaSeries> all)
    {
        InitializeComponent();

        TitleLabel.Text = title ?? "Séries";
        _category = category ?? "";
        _all = all ?? new List<DramaSeries>();

        List.ItemsSource = _items;

        Load("");
    }

    private void Load(string query)
    {
        query ??= "";
        query = query.Trim();

        IEnumerable<DramaSeries> baseSet;

        // modo especial "Top"
        if (string.Equals(_category, "__TOP__", StringComparison.OrdinalIgnoreCase))
        {
            baseSet = _all.OrderBy(x => x.TopRank == 0 ? int.MaxValue : x.TopRank);
        }
        else
        {
            baseSet = _all.Where(d =>
                d.Categories != null &&
                d.Categories.Any(c => string.Equals(c, _category, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            baseSet = baseSet.Where(d =>
                (d.Title ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (d.Subtitle ?? "").Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        var list = baseSet
            .OrderByDescending(x => x.UpdatedAtUnix)
            .ThenBy(x => x.TopRank == 0 ? int.MaxValue : x.TopRank)
            .ToList();

        _items.Clear();
        foreach (var it in list)
            _items.Add(it);
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
        => Load(e.NewTextValue);

    private async void OnBackClicked(object sender, EventArgs e)
        => await Navigation.PopAsync();

    private async void OnOpenDramaClicked(object sender, EventArgs e)
    {
        if (sender is BindableObject bo && bo.BindingContext is DramaSeries drama)
        {
            var id = drama.Id ?? "";
            if (string.IsNullOrWhiteSpace(id)) return;
            await Navigation.PushAsync(new DramaDetailsPage(id));
        }
    }
}
