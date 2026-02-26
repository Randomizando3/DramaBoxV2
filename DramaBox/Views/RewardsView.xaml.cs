using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using DramaBox.Services;
using Microsoft.Maui.Controls;

namespace DramaBox.Views;

public partial class RewardsView : ContentPage
{
    private readonly SessionService _session;
    private readonly FirebaseDatabaseService _db;

    private readonly ObservableCollection<MissionVm> _missions = new();
    private readonly ObservableCollection<ShopVm> _shop = new();

    private string _todayKey = "";
    private int _coins = 0;

    // evita múltiplas tentativas no mesmo dia enquanto a page reaparece
    private string _autoCheckinTriedForDayKey = "";
    private bool _autoCheckinRunning = false;

    // estado do checkin pra usar no tap da timeline
    private int _checkinStreak = 0;
    private bool _checkinDidToday = false;
    private int _checkinBaseStart = 1;

    public RewardsView()
    {
        InitializeComponent();

        _session = Resolve<SessionService>() ?? new SessionService();
        _db = Resolve<FirebaseDatabaseService>() ?? new FirebaseDatabaseService(new HttpClient());

        MissionsList.ItemsSource = _missions;
        ShopList.ItemsSource = _shop;
    }

    private static T? Resolve<T>() where T : class
        => Application.Current?.Handler?.MauiContext?.Services?.GetService(typeof(T)) as T;

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        _todayKey = DateTime.Now.ToString("yyyyMMdd");

        // ✅ Auto check-in: tenta uma vez por dia, silencioso
        await TryAutoCheckinIfNeededAsync();

        await ReloadAsync();
    }

    private async Task TryAutoCheckinIfNeededAsync()
    {
        try
        {
            if (_autoCheckinRunning) return;
            if (string.IsNullOrWhiteSpace(_session.UserId)) return;

            if (_autoCheckinTriedForDayKey == _todayKey)
                return;

            _autoCheckinRunning = true;
            _autoCheckinTriedForDayKey = _todayKey;

            _ = await _db.TryDailyCheckinAsync(_session.UserId, _session.IdToken);
        }
        catch
        {
            // silencioso (auto)
        }
        finally
        {
            _autoCheckinRunning = false;
        }
    }

    private async Task ReloadAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_session.UserId))
            {
                await DisplayAlert("Recompensas", "Você precisa estar logado para ver recompensas.", "OK");
                return;
            }

            await _db.EnsurePremiumConsistencyAsync(_session.UserId, _session.IdToken);
            await _db.EnsureRewardsCatalogDefaultsAsync(_session.IdToken);
            await _db.SyncApprovedManualMissionsAsync(_session.UserId, _session.IdToken);

            _coins = await _db.GetUserCoinsAsync(_session.UserId, _session.IdToken);
            CoinsLabel.Text = _coins.ToString();

            VipLabel.Text = await GetVipDaysRemainingTextAsync();

            var check = await _db.GetUserCheckinAsync(_session.UserId, _session.IdToken);
            ApplyCheckinUi(check);

            var defs = await _db.GetRewardDefinitionsAsync(_session.IdToken);

            // remove missões que você não quer mais na UI
            defs = defs
                .Where(d =>
                    !string.Equals(d.Id, "daily_login", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(d.Id, "perm_complete_profile", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var dailyState = await _db.GetUserDailyMissionsStateAsync(_session.UserId, _todayKey, _session.IdToken);
            var permState = await _db.GetUserPermanentMissionsStateAsync(_session.UserId, _session.IdToken);
            var pending = await _db.GetUserPendingManualMissionsAsync(_session.UserId, _session.IdToken);

            BuildMissions(defs, dailyState, permState, pending);

            var shop = await _db.GetRewardShopAsync(_session.IdToken);
            BuildShop(shop);
        }
        catch
        {
            // fail-safe simples
        }
    }

    private async Task<string> GetVipDaysRemainingTextAsync()
    {
        try
        {
            var uid = _session.UserId ?? "";
            if (string.IsNullOrWhiteSpace(uid)) return "0d";

            var plan = await _db.GetUserPlanAsync(uid, _session.IdToken);
            var until = await _db.GetPremiumUntilUnixAsync(uid, _session.IdToken);
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            if (!string.Equals(plan, "premium", StringComparison.OrdinalIgnoreCase))
                return "0d";

            if (until <= 0)
                return "∞";

            if (until <= now)
                return "0d";

            var sec = until - now;
            var days = (int)Math.Ceiling(sec / 86400.0);
            if (days < 0) days = 0;

            return $"{days}d";
        }
        catch
        {
            return "0d";
        }
    }

    private void BuildMissions(
        List<FirebaseDatabaseService.RewardDefinition> defs,
        Dictionary<string, FirebaseDatabaseService.RewardClaim> daily,
        Dictionary<string, FirebaseDatabaseService.RewardClaim> perm,
        Dictionary<string, FirebaseDatabaseService.RewardPendingManual> pending
    )
    {
        _missions.Clear();

        foreach (var d in defs.OrderBy(x => x.Sort).ThenBy(x => x.Title))
        {
            var isDaily = d.IsDaily;

            var claim = isDaily
                ? (daily.TryGetValue(d.Id, out var cd) ? cd : null)
                : (perm.TryGetValue(d.Id, out var cp) ? cp : null);

            var isCompleted = claim?.Done == true;

            var isPending = false;
            if (!isCompleted && d.RequiresManualApprove)
            {
                if (pending.TryGetValue(d.Id, out var p) && p != null)
                    isPending = string.Equals(p.Status, "pending", StringComparison.OrdinalIgnoreCase);
            }

            var vm = new MissionVm
            {
                Id = d.Id,
                Title = d.Title,
                Subtitle = d.Description,
                Coins = d.Coins,
                IsDaily = d.IsDaily,
                RequiresManualApprove = d.RequiresManualApprove,
                InputLabel = d.InputLabel,
                InputPlaceholder = d.InputPlaceholder,
                Icon = string.IsNullOrWhiteSpace(d.Icon) ? "⭐" : d.Icon
            };

            if (isCompleted)
            {
                vm.ActionBg = Color.FromArgb("#22C55E");
                vm.ActionFg = Colors.White;
                vm.ActionState = "done";
            }
            else if (isPending)
            {
                vm.ActionBg = Color.FromArgb("#F59E0B");
                vm.ActionFg = Colors.White;
                vm.ActionState = "pending";
            }
            else
            {
                vm.ActionBg = Color.FromArgb("#6A35DF");
                vm.ActionFg = Colors.White;
                vm.ActionState = d.RequiresManualApprove ? "send" : "claim";
            }

            _missions.Add(vm);
        }
    }

    private void BuildShop(List<FirebaseDatabaseService.RewardShopItem> items)
    {
        _shop.Clear();

        foreach (var it in items.OrderBy(x => x.Sort).ThenBy(x => x.CostCoins))
        {
            _shop.Add(new ShopVm
            {
                Id = it.Id,
                Title = it.Title,
                Subtitle = it.Description,
                CostCoins = it.CostCoins,
                VipDays = it.VipDays
            });
        }
    }

    private void ApplyCheckinUi(FirebaseDatabaseService.RewardCheckin check)
    {
        _checkinStreak = Math.Max(0, check.Streak);
        _checkinDidToday = string.Equals(check.LastDateKey, _todayKey, StringComparison.OrdinalIgnoreCase);

        // Paginação: 1-7, 8-14, 15-21...
        _checkinBaseStart = ((_checkinStreak <= 0 ? 0 : (_checkinStreak - 1)) / 7) * 7 + 1;

        var labels = new[] { Day1Text, Day2Text, Day3Text, Day4Text, Day5Text, Day6Text, Day7Text };
        for (int i = 0; i < 7; i++)
            labels[i].Text = (_checkinBaseStart + i).ToString();

        // Ícones (1..6 = coin, 7 = star) conforme o ciclo do número mostrado
        var icons = new[] { Day1Icon, Day2Icon, Day3Icon, Day4Icon, Day5Icon, Day6Icon, Day7Icon };
        for (int i = 0; i < 7; i++)
        {
            var dayNumber = _checkinBaseStart + i;
            var cycleDay = ((dayNumber - 1) % 7) + 1; // 1..7
            icons[i].Text = (cycleDay == 7) ? "⭐" : "🪙";
        }

        // Hint curto
        if (_checkinDidToday)
            CheckinHint.Text = $"Hoje já contou ✅  Sequência: {_checkinStreak} dia(s).";
        else
            CheckinHint.Text = "Ao abrir a tela, o check-in do dia é aplicado automaticamente.";

        // Cores: completo = roxo; pendente = azul claro
        var dayBorders = new[] { Day1, Day2, Day3, Day4, Day5, Day6, Day7 };
        for (int i = 0; i < 7; i++)
        {
            var dayNumber = _checkinBaseStart + i;
            var active = dayNumber <= _checkinStreak;

            dayBorders[i].BackgroundColor = active
                ? Color.FromArgb("#6A35DF")
                : Color.FromArgb("#EEF2FF");
        }
    }

    // ✅ Tap nos dias (alert)
    private async void OnDayTapped(object sender, TappedEventArgs e)
    {
        try
        {
            if (e?.Parameter == null) return;

            if (!int.TryParse(e.Parameter.ToString(), out var slot))
                return;

            if (slot < 1 || slot > 7) return;

            var dayNumber = _checkinBaseStart + (slot - 1);
            var cycleDay = ((dayNumber - 1) % 7) + 1; // 1..7

            var reward = (cycleDay == 7)
                ? "⭐ +1 dia VIP"
                : "🪙 +5 coins";

            var status = (dayNumber <= _checkinStreak)
                ? "Concluído"
                : "Pendente";

            await DisplayAlert($"Dia {dayNumber}", $"{reward}\nStatus: {status}", "OK");
        }
        catch
        {
            // ignore
        }
    }

    private async void OnBack(object sender, EventArgs e)
        => await Navigation.PopAsync();

    private async void OnRefresh(object sender, EventArgs e)
    {
        await TryAutoCheckinIfNeededAsync();
        await ReloadAsync();
    }

    private async void OnGoShop(object sender, EventArgs e)
    {
        try { await MainScroll.ScrollToAsync(ShopSection, ScrollToPosition.Start, true); }
        catch { }
    }

    private async void OnMissionAction(object sender, EventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_session.UserId)) return;
            if (sender is not Button btn) return;

            var missionId = btn.CommandParameter as string;
            if (string.IsNullOrWhiteSpace(missionId)) return;

            var vm = _missions.FirstOrDefault(x => x.Id == missionId);
            if (vm == null) return;

            if (vm.ActionState == "done" || vm.ActionState == "pending")
                return;

            if (vm.RequiresManualApprove)
            {
                var input = await DisplayPromptAsync(
                    "Curtir/seguir página oficial",
                    vm.InputLabel ?? "Informe seu @/link para eu confirmar.",
                    accept: "Enviar",
                    cancel: "Cancelar",
                    placeholder: vm.InputPlaceholder ?? "@seu_usuario",
                    maxLength: 120,
                    keyboard: Keyboard.Text);

                if (string.IsNullOrWhiteSpace(input))
                    return;

                var send = await _db.SubmitManualMissionForApprovalAsync(_session.UserId, missionId, input.Trim(), _session.IdToken);
                if (!send.ok)
                {
                    await DisplayAlert("Missão", send.message, "OK");
                    return;
                }

                await ReloadAsync();
                return;
            }

            var confirm = await DisplayAlert(
                "Completar missão (teste)",
                "Essas missões idealmente são automáticas (assistir/curtir/compartilhar). Quer marcar como concluída agora para testar?",
                "Sim", "Não");

            if (!confirm) return;

            var done = await _db.CompleteMissionAndAwardAsync(_session.UserId, missionId, _todayKey, vm.IsDaily, _session.IdToken);
            if (!done.ok)
            {
                await DisplayAlert("Missão", done.message, "OK");
                return;
            }

            await ReloadAsync();
        }
        catch { }
    }

    private async void OnBuy(object sender, EventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_session.UserId)) return;

            if (sender is not Button btn) return;
            var shopId = btn.CommandParameter as string;
            if (string.IsNullOrWhiteSpace(shopId)) return;

            var it = _shop.FirstOrDefault(x => x.Id == shopId);
            if (it == null) return;

            var ok = await DisplayAlert(
                "Confirmar troca",
                $"Trocar {it.CostCoins} coins por VIP {it.VipDays} dia(s)?",
                "Trocar", "Cancelar");

            if (!ok) return;

            var r = await _db.BuyVipWithCoinsAsync(_session.UserId, shopId, _session.IdToken);
            if (!r.ok)
            {
                await DisplayAlert("Loja", r.message, "OK");
                return;
            }

            await ReloadAsync();
        }
        catch { }
    }

    private sealed class MissionVm
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public int Coins { get; set; }
        public bool IsDaily { get; set; }
        public bool RequiresManualApprove { get; set; }
        public string? InputLabel { get; set; }
        public string? InputPlaceholder { get; set; }
        public string Icon { get; set; } = "⭐";

        public string CoinsText => $"+{Coins}";
        public string TypeText => IsDaily ? "Diária" : "Sem prazo";

        // done | pending | send | claim
        public string ActionState { get; set; } = "claim";

        public Color ActionBg { get; set; } = Color.FromArgb("#6A35DF");
        public Color ActionFg { get; set; } = Colors.White;
    }

    private sealed class ShopVm
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public int CostCoins { get; set; }
        public int VipDays { get; set; }

        public string CostText => $"-{CostCoins}";
    }
}