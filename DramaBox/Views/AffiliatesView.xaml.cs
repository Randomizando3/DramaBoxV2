using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using DramaBox.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace DramaBox.Views;

public partial class AffiliatesView : ContentPage
{
    private readonly SessionService _session;
    private readonly FirebaseDatabaseService _db;

    private string _code = "";
    private string _link = "";

    private const string LandingBaseUrl = "https://izzihub.com.br";
    private const string LandingPagePath = "/pagina.php";

    public AffiliatesView()
    {
        InitializeComponent();

        _session = Resolve<SessionService>() ?? new SessionService();
        _db = Resolve<FirebaseDatabaseService>() ?? new FirebaseDatabaseService(new HttpClient());
    }

    private static T? Resolve<T>() where T : class
        => Application.Current?.Handler?.MauiContext?.Services?.GetService(typeof(T)) as T;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await ReloadAsync();
    }

    private static string BuildLandingLink(string code)
    {
        code = (code ?? "").Trim();
        if (code.Length == 0) return "";
        return $"{LandingBaseUrl}{LandingPagePath}?ref={Uri.EscapeDataString(code)}";
    }

    private async Task ReloadAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_session.UserId))
            {
                await DisplayAlert("Afiliados", "Você precisa estar logado.", "OK");
                return;
            }

            // 1) garante afiliado (gera/recupera CODE)
            var res = await _db.EnsureAffiliateAsync(_session.UserId, _session.IdToken);
            _code = (res.code ?? "").Trim().ToUpperInvariant();
            _link = BuildLandingLink(_code);

            CodeLabel.Text = string.IsNullOrWhiteSpace(_code) ? "—" : _code;
            LinkLabel.Text = string.IsNullOrWhiteSpace(_link) ? "—" : _link;

            // 2) stats (bonusCoins é o acumulado creditado)
            var stats = await _db.GetAffiliateStatsAsync(_session.UserId, _session.IdToken);
            var bonus = stats?.BonusCoins ?? 0;
            BonusLabel.Text = $"{bonus} ??";

            // 3) LEADS: fonte da verdade (somente bucket do SEU CODE)
            if (!string.IsNullOrWhiteSpace(_code))
            {
                var leads = await _db.GetAffiliateLeadsByBucketAsync(_code, _session.IdToken);

                var pending = leads.Count(l => string.Equals((l.Status ?? "").Trim(), "pending", StringComparison.OrdinalIgnoreCase));
                var confirmed = leads.Count(l => string.Equals((l.Status ?? "").Trim(), "confirmed", StringComparison.OrdinalIgnoreCase));

                // ? Clicks = pendentes (como você pediu)
                ClicksLabel.Text = pending.ToString();
                SignupsLabel.Text = confirmed.ToString();
            }
            else
            {
                ClicksLabel.Text = "0";
                SignupsLabel.Text = "0";
            }

            // placeholder (pra você usar em breve)
            BalanceReaisLabel.Text = "R$ 0,00";
        }
        catch
        {
            // fail-safe
        }
    }

    private async void OnBack(object sender, EventArgs e)
        => await Navigation.PopAsync();

    private async void OnRefresh(object sender, EventArgs e)
        => await ReloadAsync();

    private async void OnCopyCode(object sender, EventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_code))
            {
                await DisplayAlert("Afiliados", "Sem código ainda.", "OK");
                return;
            }

            await Clipboard.Default.SetTextAsync(_code);
            await DisplayAlert("Afiliados", "Código copiado.", "OK");
        }
        catch { }
    }

    private async void OnCopyLink(object sender, EventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_link))
            {
                await DisplayAlert("Afiliados", "Sem link ainda.", "OK");
                return;
            }

            await Clipboard.Default.SetTextAsync(_link);
            await DisplayAlert("Afiliados", "Link copiado.", "OK");
        }
        catch { }
    }

    private async void OnShare(object sender, EventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_link))
            {
                await DisplayAlert("Afiliados", "Sem link ainda.", "OK");
                return;
            }

            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Title = "DramaBox • Meu link",
                Text = $"Baixa o DramaBox por aqui: {_link}"
            });
        }
        catch { }
    }
}