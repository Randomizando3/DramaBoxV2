using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using DramaBox.Models;
using DramaBox.Services;

namespace DramaBox.Views;

public partial class CreationView : ContentPage
{
    private const double MinPayoutCents = 5000; // R$ 50,00

    private readonly SessionService _session;
    private readonly FirebaseDatabaseService _db;

    public ObservableCollection<MySeriesRow> MySeries { get; } = new();
    private double _currentRevenueCents;

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

            var cover = string.IsNullOrWhiteSpace(s.CoverUrl) ? (s.PosterUrl ?? "") : s.CoverUrl;
            if (string.IsNullOrWhiteSpace(cover))
                cover = null;

            var sub = (s.Subtitle ?? "").Trim();
            var epsText = string.IsNullOrWhiteSpace(sub) ? $"{eps.Count} eps" : $"{sub} • {eps.Count} eps";

            MySeries.Add(new MySeriesRow
            {
                SeriesId = s.Id,
                Title = s.Title ?? "",
                Subtitle = sub,
                CoverUrl = cover,
                EpisodesText = epsText
            });
        }

        var cents = await _db.GetAsync<double?>($"community/earnings/creators/{uid}/centsTotal", _session.IdToken) ?? 0.0;
        _currentRevenueCents = Math.Max(0, cents);

        RevenueLabel.Text = ToBRLFromCents(_currentRevenueCents);
        await LoadPayoutStatusAsync(uid);

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

    private async Task LoadPayoutStatusAsync(string uid)
    {
        if (PayoutStatusLabel == null)
            return;

        var latest = await _db.GetLatestUserPayoutRequestAsync(uid, _session.IdToken);
        if (latest == null)
        {
            PayoutStatusLabel.Text = "Ultimo saque: sem solicitacoes";
            return;
        }

        var amount = ToBRLFromCents(latest.AmountCents);
        var status = (latest.Status ?? "pending").Trim().ToLowerInvariant();

        if (status == "approved")
        {
            var when = latest.DecidedAtUnix > 0
                ? DateTimeOffset.FromUnixTimeSeconds(latest.DecidedAtUnix).ToLocalTime().ToString("dd/MM/yyyy HH:mm")
                : "-";

            PayoutStatusLabel.Text = $"Ultimo saque: aprovado ({amount}) em {when}";
            return;
        }

        if (status == "rejected")
        {
            var when = latest.DecidedAtUnix > 0
                ? DateTimeOffset.FromUnixTimeSeconds(latest.DecidedAtUnix).ToLocalTime().ToString("dd/MM/yyyy HH:mm")
                : "-";

            PayoutStatusLabel.Text = $"Ultimo saque: reprovado ({amount}) em {when}";
            return;
        }

        PayoutStatusLabel.Text = $"Ultimo saque: pending ({amount})";
    }

    private static string ToBRLFromCents(double cents)
    {
        var br = CultureInfo.GetCultureInfo("pt-BR");
        var reais = cents / 100.0;
        return reais.ToString("C", br);
    }

    private async void OnRefreshClicked(object sender, EventArgs e)
        => await LoadAsync();

    private async void OnCreateSeriesTapped(object sender, EventArgs e)
        => await CreateSeriesFlowAsync();

    private async void OnCreateSeriesClicked(object sender, EventArgs e)
        => await CreateSeriesFlowAsync();

    private async Task CreateSeriesFlowAsync()
    {
        var uid = _session.UserId ?? "";
        if (string.IsNullOrWhiteSpace(uid))
        {
            await DisplayAlert("Conta", "Voce precisa estar logado.", "OK");
            return;
        }

        var title = await DisplayPromptAsync("Nova serie", "Titulo da serie:");
        if (string.IsNullOrWhiteSpace(title)) return;

        var subtitle = await DisplayPromptAsync("Nova serie", "Subtitulo (opcional):") ?? "";

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

        await Navigation.PushAsync(new CreatorSeriesEditorPage(series.Id));
    }

    private async void OnSeriesSelected(object sender, SelectionChangedEventArgs e)
    {
        var row = e.CurrentSelection?.FirstOrDefault() as MySeriesRow;
        ((CollectionView)sender).SelectedItem = null;
        if (row == null) return;

        await Navigation.PushAsync(new CreatorSeriesEditorPage(row.SeriesId));
    }

    private async void OnRevenueTapped(object sender, TappedEventArgs e)
    {
        var uid = _session.UserId ?? "";
        if (string.IsNullOrWhiteSpace(uid))
        {
            await DisplayAlert("Saques", "Voce precisa estar logado.", "OK");
            return;
        }

        if (_currentRevenueCents < MinPayoutCents)
        {
            await DisplayAlert(
                "Saques",
                $"Saldo atual: {ToBRLFromCents(_currentRevenueCents)}.\nMinimo para solicitar: {ToBRLFromCents(MinPayoutCents)}.",
                "OK");
            return;
        }

        var pix = await DisplayPromptAsync(
            "Solicitar saque",
            "Informe a chave PIX para pagamento:",
            accept: "Continuar",
            cancel: "Cancelar",
            placeholder: "email, CPF, telefone ou aleatoria");

        if (string.IsNullOrWhiteSpace(pix))
            return;

        var amountText = ToBRLFromCents(_currentRevenueCents);
        var ok = await DisplayAlert(
            "Confirmar saque",
            $"Solicitar saque de {amountText}?",
            "Solicitar",
            "Cancelar");

        if (!ok) return;

        var req = await _db.CreatePayoutRequestAsync(
            uid: uid,
            creatorName: _session.Profile?.Nome ?? "",
            creatorEmail: _session.Email ?? "",
            pixKey: pix.Trim(),
            amountCents: _currentRevenueCents,
            idToken: _session.IdToken
        );

        if (!req.ok)
        {
            await DisplayAlert("Saques", req.message, "OK");
            return;
        }

        await DisplayAlert("Saques", "Solicitacao enviada com status pending.", "OK");
        await LoadAsync();
    }

    public sealed class MySeriesRow
    {
        public string SeriesId { get; set; } = "";
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string? CoverUrl { get; set; }
        public string EpisodesText { get; set; } = "";
    }
}
