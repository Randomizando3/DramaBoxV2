// Views/Upgrade.xaml.cs
using System;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using DramaBox.Models;
using DramaBox.Services;

namespace DramaBox.Views;

public partial class Upgrade : ContentPage
{
    private readonly SessionService _session;
    private readonly FirebaseDatabaseService _db;

    private UserProfile? _profile;

    public Upgrade()
    {
        InitializeComponent();

        _session = Resolve<SessionService>() ?? new SessionService();
        _db = Resolve<FirebaseDatabaseService>() ?? CreateDbFallback();
    }

    public Upgrade(SessionService session, FirebaseDatabaseService db)
    {
        InitializeComponent();
        _session = session;
        _db = db;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private static T? Resolve<T>() where T : class
        => Application.Current?.Handler?.MauiContext?.Services?.GetService(typeof(T)) as T;

    private static FirebaseDatabaseService CreateDbFallback()
        => new FirebaseDatabaseService(new System.Net.Http.HttpClient());

    private static string NameFromEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return "Usuário";

        var at = email.IndexOf('@');
        if (at > 0)
            return email.Substring(0, at);

        return email;
    }

    private async Task LoadAsync()
    {
        var uid = _session.UserId ?? "";
        var token = _session.IdToken;

        if (string.IsNullOrWhiteSpace(uid))
            return;

        _profile = await _db.GetUserProfileAsync(uid, token);

        if (_profile == null)
        {
            var fallbackName =
                _session.Profile?.Nome
                ?? NameFromEmail(_session.Email);

            _profile = new UserProfile
            {
                UserId = uid,
                Email = _session.Email ?? "",
                Nome = fallbackName,
                Plano = "free",
                PremiumUntilUnix = 0,
                FotoUrl = ""
            };

            await _db.UpsertUserProfileAsync(uid, _profile, token);
        }

        // ? garante consistência (se premium expirou, volta free; se premium válido, mantém)
        await _db.EnsurePremiumConsistencyAsync(uid, token);

        _profile = await _db.GetUserProfileAsync(uid, token);
        _session.SetProfile(_profile);
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    private async void OnSelectFreeClicked(object sender, EventArgs e)
    {
        await SetPlanAsync("free");
    }

    private async void OnSelectPremiumClicked(object sender, EventArgs e)
    {
        var ok = await DisplayAlert("Premium", "Selecionar Premium por 30 dias? (mock, sem pagamento)", "Sim", "Cancelar");
        if (!ok) return;

        await SetPlanAsync("premium");
    }

    private async Task SetPlanAsync(string plan)
    {
        try
        {
            var uid = _session.UserId ?? "";
            var token = _session.IdToken;

            if (string.IsNullOrWhiteSpace(uid))
                return;

            plan = (plan ?? "").Trim().ToLowerInvariant();
            if (plan != "free" && plan != "premium") plan = "free";

            // ? regra:
            // - free: PremiumUntilUnix = 0
            // - premium: PremiumUntilUnix = now + 30 dias
            long premiumUntil = 0;

            if (plan == "premium")
                premiumUntil = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();

            var resp = await _db.PatchAsync($"users/{uid}/profile", new
            {
                Plano = plan,
                PremiumUntilUnix = premiumUntil
            }, token);

            if (!resp.ok)
            {
                await DisplayAlert("Planos", resp.message, "OK");
                return;
            }

            if (_profile != null)
            {
                _profile.Plano = plan;
                _profile.PremiumUntilUnix = premiumUntil;
            }

            _session.SetProfile(_profile);

            await DisplayAlert("Planos", plan == "premium"
                ? "Premium ativado por 30 dias!"
                : "Plano atualizado para Free.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Planos", $"Erro ao atualizar plano: {ex.Message}", "OK");
        }
    }
}