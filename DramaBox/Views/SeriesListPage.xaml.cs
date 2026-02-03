using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.Maui.Controls;
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

        IEnumerable<DramaSeries> baseSet = _all;

        // filtro por categoria
        if (!string.IsNullOrWhiteSpace(_category))
        {
            baseSet = baseSet.Where(d =>
                d.Categories != null &&
                d.Categories.Any(c => string.Equals(c, _category, StringComparison.OrdinalIgnoreCase)));
        }

        // filtro por busca (title/subtitle)
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
        // ? IMPORTANTE: sua tela de detalhes está esperando string (dramaId)
        if (sender is BindableObject bo && bo.BindingContext is DramaSeries drama)
        {
            var id = drama.Id ?? "";
            if (string.IsNullOrWhiteSpace(id))
                return;

            await Navigation.PushAsync(new DramaDetailsPage(id));
        }
    }
}
