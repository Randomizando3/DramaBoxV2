using System;
using Microsoft.Maui.Controls;
using DramaBox.Views;

namespace DramaBox;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
    }

    private async void OnOpenLoginClicked(object sender, EventArgs e)
    {
        // Agora Navigation NÃO será null, porque App.MainPage é NavigationPage
        await Navigation.PushAsync(new LoginView());
    }
}
