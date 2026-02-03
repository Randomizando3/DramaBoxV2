using System;
using Microsoft.Maui.Controls;

namespace DramaBox.Views;

public partial class PlayerPage : ContentPage
{
    private string _url = "";

    public PlayerPage()
    {
        InitializeComponent();
    }

    public PlayerPage(string title, string url) : this()
    {
        Title = title ?? "";
        _url = url ?? "";
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (string.IsNullOrWhiteSpace(_url))
            return;

        // Source do MediaElement aceita Uri/MediaSource dependendo da versão;
        // essa forma funciona bem:
        Player.Source = _url;
        Player.Play();
    }

    protected override void OnDisappearing()
    {
        try { Player?.Stop(); } catch { }
        base.OnDisappearing();
    }

    private async void OnBackClicked(object sender, EventArgs e)
        => await Navigation.PopAsync();
}
