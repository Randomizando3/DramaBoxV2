using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using DramaBox.Models;
using DramaBox.Services;

namespace DramaBox.Views;

public partial class ProfileView : ContentPage
{
    private readonly SessionService _session;
    private readonly FirebaseDatabaseService _db;

    private UserProfile? _profile;

    public ProfileView()
    {
        InitializeComponent();

        _session = Resolve<SessionService>() ?? new SessionService();
        _db = Resolve<FirebaseDatabaseService>() ?? new FirebaseDatabaseService(new System.Net.Http.HttpClient());
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadProfileAsync();
        await LoadStatsAsync();
    }

    private static T? Resolve<T>() where T : class
        => Application.Current?.Handler?.MauiContext?.Services?.GetService(typeof(T)) as T;

    private static string NameFromEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "Usuário";
        var at = email.IndexOf('@');
        return at > 0 ? email.Substring(0, at) : email;
    }

    private static string BuildInitials(string nameOrEmail)
    {
        if (string.IsNullOrWhiteSpace(nameOrEmail)) return "U";

        var parts = nameOrEmail.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
            return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}";

        return char.ToUpperInvariant(nameOrEmail.Trim()[0]).ToString();
    }

    private async Task LoadProfileAsync()
    {
        try
        {
            var uid = _session.UserId ?? "";
            var token = _session.IdToken;

            if (string.IsNullOrWhiteSpace(uid))
                return;

            _profile = await _db.GetUserProfileAsync(uid, token);

            if (_profile == null)
            {
                var fallbackName = _session.Profile?.Nome ?? NameFromEmail(_session.Email);

                _profile = new UserProfile
                {
                    UserId = uid,
                    Email = _session.Email ?? "",
                    Nome = fallbackName,
                    Plano = "free",
                    FotoUrl = ""
                };

                await _db.UpsertUserProfileAsync(uid, _profile, token);
            }

            _session.SetProfile(_profile);

            // UI: nome
            NameLabel.Text = string.IsNullOrWhiteSpace(_profile.Nome) ? "—" : _profile.Nome;

            // UI: premium badge
            var isPremium = string.Equals(_profile.Plano, "premium", StringComparison.OrdinalIgnoreCase);
            PremiumBadge.IsVisible = isPremium;

            // UI: avatar
            if (!string.IsNullOrWhiteSpace(_profile.FotoUrl))
            {
                try
                {
                    ProfileImage.Source = ImageSource.FromUri(new Uri(_profile.FotoUrl));
                    ProfileImage.IsVisible = true;
                    InitialsLabel.IsVisible = false;
                }
                catch
                {
                    ProfileImage.IsVisible = false;
                    InitialsLabel.IsVisible = true;
                }
            }
            else
            {
                ProfileImage.IsVisible = false;
                InitialsLabel.IsVisible = true;
                InitialsLabel.Text = BuildInitials(_profile.Nome ?? _profile.Email ?? "U");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Perfil", $"Erro ao carregar perfil: {ex.Message}", "OK");
        }
    }

    private async Task LoadStatsAsync()
    {
        try
        {
            var uid = _session.UserId ?? "";
            var token = _session.IdToken;
            if (string.IsNullOrWhiteSpace(uid))
                return;

            // 1) Curtidas recebidas + séries/eps publicados (baseado nas séries do criador)
            var mySeries = await _db.GetCreatorCommunitySeriesAsync(uid, token);

            double likes = 0;
            int episodesPublished = 0;
            int seriesPublished = mySeries.Count(s => s != null && s.IsPublished);

            foreach (var s in mySeries)
            {
                if (s == null || string.IsNullOrWhiteSpace(s.Id))
                    continue;

                var m = await _db.GetAsync<CommunitySeriesMetrics>($"community/metrics/series/{s.Id}", token)
                        ?? new CommunitySeriesMetrics();

                likes += m.Likes;

                // eps publicados = quantidade de episódios cadastrados nessa série
                var eps = await _db.GetCommunityEpisodesAsync(s.Id, token);
                episodesPublished += eps.Count;
            }

            LikesReceivedLabel.Text = $"{FormatCompact(likes)} curtidas";
            EpisodesPublishedLabel.Text = $"{episodesPublished.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"))} ep publicados";

            // 2) Salvos (playlist do usuário)
            var playlist = await _db.GetPlaylistMapAsync(uid, token);
            var savedCount = playlist?.Count ?? 0;
            SavedLabel.Text = $"{savedCount.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"))} salvos";

            // (opcional) se quiser usar seriesPublished em algum label futuro, já está pronto.
            _ = seriesPublished;
        }
        catch (Exception ex)
        {
            await DisplayAlert("Perfil", $"Erro ao carregar estatísticas: {ex.Message}", "OK");
        }
    }

    // 12.4k style (igual ao mock)
    private static string FormatCompact(double value)
    {
        if (value < 0) value = 0;

        if (value >= 1_000_000)
            return (value / 1_000_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "M";

        if (value >= 1_000)
            return (value / 1_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "k";

        return ((long)Math.Round(value)).ToString("N0", CultureInfo.GetCultureInfo("pt-BR"));
    }

    // mantém sua função
    private async void OnOpenUpgradeClicked(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new Upgrade());
    }

    // mantém sua função
    private async void OnLogoutClicked(object sender, EventArgs e)
    {
        var ok = await DisplayAlert("Sair", "Deseja sair da conta?", "Sim", "Cancelar");
        if (!ok) return;

        _session.Clear();
        Application.Current!.MainPage = new NavigationPage(new LoginView());
    }

    // mantém sua função
    private async void OnChangePhotoClicked(object sender, EventArgs e)
    {
        await DisplayAlert("Foto", "Em breve: selecionar e enviar foto.", "OK");
    }

    // (mock) engrenagem do topo
    private async void OnSettingsClicked(object sender, EventArgs e)
    {
        await DisplayAlert("Configurações", "Em breve.", "OK");
    }
}
