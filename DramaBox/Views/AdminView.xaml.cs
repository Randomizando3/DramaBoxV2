using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using DramaBox.Models;
using DramaBox.Services;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

namespace DramaBox.Views;

public partial class AdminView : ContentPage
{
    private const string SectionMonetization = "monetization";
    private const string SectionUsers = "users";
    private const string SectionCommunity = "community";
    private const string SectionDramas = "dramas";

    private readonly SessionService _session;
    private readonly FirebaseDatabaseService _db;
    private readonly FirebaseAuthService _auth;
    private readonly FirebaseStorageService _st;

    private readonly ObservableCollection<PayoutRowVm> _payouts = new();
    private readonly ObservableCollection<UserRowVm> _users = new();
    private readonly ObservableCollection<CommunityRowVm> _communityRows = new();
    private readonly ObservableCollection<DramaRowVm> _dramaRows = new();

    private List<UserRowVm> _allUsers = new();
    private bool _isLoading;
    private string _activeSection = SectionMonetization;

    public AdminView()
    {
        InitializeComponent();

        _session = Resolve<SessionService>() ?? new SessionService();
        _db = Resolve<FirebaseDatabaseService>() ?? new FirebaseDatabaseService(new HttpClient());
        _auth = Resolve<FirebaseAuthService>() ?? new FirebaseAuthService(new HttpClient());
        _st = Resolve<FirebaseStorageService>() ?? new FirebaseStorageService(new HttpClient());

        PayoutList.ItemsSource = _payouts;
        UsersList.ItemsSource = _users;
        CommunitySeriesList.ItemsSource = _communityRows;
        DramaList.ItemsSource = _dramaRows;

        SetActiveSection(SectionMonetization);
    }

    private static T? Resolve<T>() where T : class
        => Application.Current?.Handler?.MauiContext?.Services?.GetService(typeof(T)) as T;

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (!await EnsureAdminAccessAsync())
            return;

        await ReloadAllAsync();
    }

    private async Task<bool> EnsureAdminAccessAsync()
    {
        if (_session.IsAdmin)
            return true;

        await DisplayAlert("Admin", "Acesso restrito.", "OK");
        try { await Shell.Current.GoToAsync("//discover"); } catch { }
        return false;
    }

    private async Task ReloadAllAsync()
    {
        if (_isLoading) return;
        _isLoading = true;

        try
        {
            await LoadPayoutsAsync();
            await LoadUsersAsync();
            await LoadCommunityAsync();
            await LoadDramasAsync();
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task LoadPayoutsAsync()
    {
        var list = await _db.GetAllPayoutRequestsAsync(_session.IdToken);

        _payouts.Clear();
        foreach (var item in list)
        {
            var who = !string.IsNullOrWhiteSpace(item.CreatorName)
                ? item.CreatorName
                : (!string.IsNullOrWhiteSpace(item.CreatorEmail) ? item.CreatorEmail : item.Uid);

            _payouts.Add(new PayoutRowVm
            {
                RequestId = item.RequestId,
                MainText = $"{who} - {ToBrl(item.AmountCents)}",
                SecondaryText = $"UID: {item.Uid} - {UnixToText(item.CreatedAtUnix)}",
                StatusText = BuildPayoutStatusText(item),
                CanDecide = string.Equals(item.Status, "pending", StringComparison.OrdinalIgnoreCase)
            });
        }
    }

    private async Task LoadUsersAsync()
    {
        var list = await _db.GetAllUsersForAdminAsync(_session.IdToken);

        _allUsers = list.Select(x =>
        {
            var display = !string.IsNullOrWhiteSpace(x.Nome)
                ? x.Nome
                : (!string.IsNullOrWhiteSpace(x.Email) ? x.Email : x.Uid);

            var sub = $"UID: {x.Uid} - Plano: {x.Plano} - Coins: {x.Coins}";
            if (!string.IsNullOrWhiteSpace(x.Telefone))
                sub += $" - Tel: {x.Telefone}";

            return new UserRowVm
            {
                Uid = x.Uid,
                MainText = display,
                SubText = sub,
                StatusText = BuildUserStatusText(x.Moderation),
                CanModerate = !string.Equals(x.Uid, _session.UserId, StringComparison.Ordinal)
            };
        })
        .OrderBy(x => x.MainText)
        .ToList();

        ApplyUserFilter(SearchUserEntry?.Text);
    }

    private async Task LoadCommunityAsync()
    {
        var list = await _db.GetAllCommunitySeriesAdminAsync(_session.IdToken);

        _communityRows.Clear();
        foreach (var series in list)
        {
            var seriesId = series.Id ?? "";
            if (string.IsNullOrWhiteSpace(seriesId))
                continue;

            var eps = await _db.GetCommunityEpisodesAsync(seriesId, _session.IdToken);

            _communityRows.Add(new CommunityRowVm
            {
                SeriesId = seriesId,
                MainText = $"{series.Title} - {eps.Count} eps",
                SubText = $"Criador: {series.CreatorName} - ID: {seriesId}"
            });
        }
    }

    private async Task LoadDramasAsync()
    {
        var list = await _db.GetAllDramasAsync(_session.IdToken);

        _dramaRows.Clear();
        foreach (var drama in list)
        {
            var cats = (drama.Categories ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
            var catText = cats.Length > 0 ? string.Join(", ", cats) : "sem categorias";

            _dramaRows.Add(new DramaRowVm
            {
                DramaId = drama.Id ?? "",
                MainText = drama.Title,
                SubText = $"ID: {drama.Id} - {catText}"
            });
        }
    }

    private void ApplyUserFilter(string? query)
    {
        query = (query ?? "").Trim();
        IEnumerable<UserRowVm> src = _allUsers;

        if (!string.IsNullOrWhiteSpace(query))
        {
            src = src.Where(x =>
                (x.MainText ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (x.SubText ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (x.Uid ?? "").Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        _users.Clear();
        foreach (var it in src)
            _users.Add(it);
    }

    private static string UnixToText(long unix)
    {
        if (unix <= 0) return "-";
        return DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    }

    private static string ToBrl(double cents)
    {
        var br = CultureInfo.GetCultureInfo("pt-BR");
        return (Math.Max(0, cents) / 100.0).ToString("C", br);
    }

    private static string BuildPayoutStatusText(FirebaseDatabaseService.CreatorPayoutRequest item)
    {
        var status = (item.Status ?? "pending").Trim().ToLowerInvariant();
        return status switch
        {
            "approved" => $"Status: aprovado em {UnixToText(item.DecidedAtUnix)}",
            "rejected" => $"Status: reprovado em {UnixToText(item.DecidedAtUnix)}",
            _ => "Status: pending"
        };
    }

    private static string BuildUserStatusText(FirebaseDatabaseService.UserModeration? mod)
    {
        if (mod == null)
            return "Status: ativo";

        var status = (mod.Status ?? "active").Trim().ToLowerInvariant();
        return status switch
        {
            "banned_until" => mod.BanUntilUnix > 0
                ? $"Status: banido ate {UnixToText(mod.BanUntilUnix)}"
                : "Status: banido",
            "suspended_permanent" => "Status: suspensao permanente",
            "removed" => "Status: removido",
            _ => "Status: ativo"
        };
    }

    private static string NormalizePhone(string? raw)
        => new string((raw ?? "").Where(char.IsDigit).ToArray());

    private async Task ReloadOnlyUsersAsync()
    {
        await LoadUsersAsync();
    }

    private void SetActiveSection(string section)
    {
        _activeSection = section switch
        {
            SectionUsers => SectionUsers,
            SectionCommunity => SectionCommunity,
            SectionDramas => SectionDramas,
            _ => SectionMonetization
        };

        if (MonetizationSection != null)
            MonetizationSection.IsVisible = _activeSection == SectionMonetization;

        if (UsersSection != null)
            UsersSection.IsVisible = _activeSection == SectionUsers;

        if (CommunitySection != null)
            CommunitySection.IsVisible = _activeSection == SectionCommunity;

        if (DramasSection != null)
            DramasSection.IsVisible = _activeSection == SectionDramas;

        ApplyTabButtonStyle(TabMonetizationButton, _activeSection == SectionMonetization);
        ApplyTabButtonStyle(TabUsersButton, _activeSection == SectionUsers);
        ApplyTabButtonStyle(TabCommunityButton, _activeSection == SectionCommunity);
        ApplyTabButtonStyle(TabDramasButton, _activeSection == SectionDramas);
    }

    private static void ApplyTabButtonStyle(Button? button, bool isActive)
    {
        if (button == null) return;

        if (isActive)
        {
            button.BackgroundColor = Color.FromArgb("#1D4ED8");
            button.TextColor = Colors.White;
        }
        else
        {
            button.BackgroundColor = Color.FromArgb("#E2E8F0");
            button.TextColor = Color.FromArgb("#0F172A");
        }
    }

    private async void OnRefreshClicked(object sender, EventArgs e)
        => await ReloadAllAsync();

    private void OnUserSearchChanged(object sender, TextChangedEventArgs e)
        => ApplyUserFilter(e.NewTextValue);

    private void OnShowMonetizationClicked(object sender, EventArgs e)
        => SetActiveSection(SectionMonetization);

    private void OnShowUsersClicked(object sender, EventArgs e)
        => SetActiveSection(SectionUsers);

    private void OnShowCommunityClicked(object sender, EventArgs e)
        => SetActiveSection(SectionCommunity);

    private void OnShowDramasClicked(object sender, EventArgs e)
        => SetActiveSection(SectionDramas);

    private async void OnApprovePayoutClicked(object sender, EventArgs e)
    {
        if (sender is not Button b || b.CommandParameter is not string requestId) return;

        var yes = await DisplayAlert("Pagamento", "Aprovar esta solicitacao?", "Aprovar", "Cancelar");
        if (!yes) return;

        var note = await DisplayPromptAsync("Pagamento", "Observacao (opcional):", initialValue: "");
        var r = await _db.DecidePayoutRequestAsync(
            requestId: requestId,
            adminUid: _session.UserId,
            approve: true,
            adminNote: note,
            idToken: _session.IdToken
        );

        await DisplayAlert("Pagamento", r.ok ? "Solicitacao aprovada." : r.message, "OK");
        await ReloadAllAsync();
    }

    private async void OnRejectPayoutClicked(object sender, EventArgs e)
    {
        if (sender is not Button b || b.CommandParameter is not string requestId) return;

        var yes = await DisplayAlert("Pagamento", "Reprovar esta solicitacao?", "Reprovar", "Cancelar");
        if (!yes) return;

        var note = await DisplayPromptAsync("Pagamento", "Motivo (opcional):", initialValue: "");
        var r = await _db.DecidePayoutRequestAsync(
            requestId: requestId,
            adminUid: _session.UserId,
            approve: false,
            adminNote: note,
            idToken: _session.IdToken
        );

        await DisplayAlert("Pagamento", r.ok ? "Solicitacao reprovada." : r.message, "OK");
        await ReloadAllAsync();
    }

    private async void OnAddUserClicked(object sender, EventArgs e)
    {
        var email = await DisplayPromptAsync("Adicionar usuario", "Email:");
        if (string.IsNullOrWhiteSpace(email)) return;

        var password = await DisplayPromptAsync("Adicionar usuario", "Senha (min. 6):");
        if (string.IsNullOrWhiteSpace(password)) return;

        var name = await DisplayPromptAsync("Adicionar usuario", "Nome:");
        if (string.IsNullOrWhiteSpace(name)) name = email;

        var phone = await DisplayPromptAsync("Adicionar usuario", "Telefone (opcional):") ?? "";
        var phoneDigits = NormalizePhone(phone);

        var (ok, msg, auth) = await _auth.SignUpAsync(email.Trim(), password);
        if (!ok || auth == null)
        {
            await DisplayAlert("Usuarios", msg, "OK");
            return;
        }

        var profile = new UserProfile
        {
            UserId = auth.LocalId,
            Email = auth.Email,
            Nome = name.Trim(),
            Telefone = phoneDigits,
            Plano = "free",
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        var saved = await _db.UpsertUserProfileAsync(auth.LocalId, profile, _session.IdToken);
        if (!saved.ok)
        {
            await DisplayAlert("Usuarios", saved.message, "OK");
            return;
        }

        await _db.SetUserModerationAsync(
            uid: auth.LocalId,
            status: "active",
            adminUid: _session.UserId,
            reason: "Criado manualmente pelo admin.",
            idToken: _session.IdToken
        );

        await DisplayAlert("Usuarios", $"Usuario criado com sucesso.\nUID: {auth.LocalId}", "OK");
        await ReloadOnlyUsersAsync();
    }

    private async void OnActivateUserClicked(object sender, EventArgs e)
    {
        if (sender is not Button b || b.CommandParameter is not string uid) return;
        if (string.Equals(uid, _session.UserId, StringComparison.Ordinal)) return;

        var r = await _db.SetUserModerationAsync(
            uid: uid,
            status: "active",
            adminUid: _session.UserId,
            reason: "Usuario reativado pelo admin.",
            idToken: _session.IdToken
        );

        await DisplayAlert("Usuarios", r.ok ? "Usuario ativado." : r.message, "OK");
        await ReloadOnlyUsersAsync();
    }

    private async void OnBanUserClicked(object sender, EventArgs e)
    {
        if (sender is not Button b || b.CommandParameter is not string uid) return;
        if (string.Equals(uid, _session.UserId, StringComparison.Ordinal)) return;

        var yes = await DisplayAlert("Usuarios", "Aplicar banimento de 7 dias?", "Banir", "Cancelar");
        if (!yes) return;

        var reason = await DisplayPromptAsync("Usuarios", "Motivo (opcional):", initialValue: "");
        var r = await _db.BanUserForDaysAsync(
            uid: uid,
            adminUid: _session.UserId,
            days: 7,
            reason: reason,
            idToken: _session.IdToken
        );

        await DisplayAlert("Usuarios", r.ok ? "Usuario banido por 7 dias." : r.message, "OK");
        await ReloadOnlyUsersAsync();
    }

    private async void OnSuspendUserClicked(object sender, EventArgs e)
    {
        if (sender is not Button b || b.CommandParameter is not string uid) return;
        if (string.Equals(uid, _session.UserId, StringComparison.Ordinal)) return;

        var yes = await DisplayAlert("Usuarios", "Suspender permanentemente este usuario?", "Suspender", "Cancelar");
        if (!yes) return;

        var reason = await DisplayPromptAsync("Usuarios", "Motivo (opcional):", initialValue: "");
        var r = await _db.SuspendUserPermanentlyAsync(
            uid: uid,
            adminUid: _session.UserId,
            reason: reason,
            idToken: _session.IdToken
        );

        await DisplayAlert("Usuarios", r.ok ? "Usuario suspenso permanentemente." : r.message, "OK");
        await ReloadOnlyUsersAsync();
    }

    private async void OnRemoveUserClicked(object sender, EventArgs e)
    {
        if (sender is not Button b || b.CommandParameter is not string uid) return;
        if (string.Equals(uid, _session.UserId, StringComparison.Ordinal))
        {
            await DisplayAlert("Usuarios", "Nao e possivel remover o proprio usuario admin.", "OK");
            return;
        }

        var yes = await DisplayAlert("Usuarios", "Remover usuario? Isso apaga os dados em /users/{uid}.", "Remover", "Cancelar");
        if (!yes) return;

        var reason = await DisplayPromptAsync("Usuarios", "Motivo (opcional):", initialValue: "");
        var r = await _db.RemoveUserAsync(
            uid: uid,
            adminUid: _session.UserId,
            reason: reason,
            idToken: _session.IdToken
        );

        await DisplayAlert("Usuarios", r.ok ? "Usuario removido." : r.message, "OK");
        await ReloadOnlyUsersAsync();
    }

    private async void OnDeleteCommunityEpisodeClicked(object sender, EventArgs e)
    {
        if (sender is not Button b || b.CommandParameter is not CommunityRowVm row) return;

        var numStr = await DisplayPromptAsync("Comunidade", $"Numero do episodio para remover em \"{row.MainText}\":");
        if (string.IsNullOrWhiteSpace(numStr)) return;
        if (!int.TryParse(numStr.Trim(), out var number) || number <= 0)
        {
            await DisplayAlert("Comunidade", "Numero invalido.", "OK");
            return;
        }

        var episodes = await _db.GetCommunityEpisodesAsync(row.SeriesId, _session.IdToken);
        var episode = episodes.FirstOrDefault(x => x.Number == number);
        if (episode == null || string.IsNullOrWhiteSpace(episode.Id))
        {
            await DisplayAlert("Comunidade", "Episodio nao encontrado.", "OK");
            return;
        }

        var yes = await DisplayAlert("Comunidade", $"Remover episodio {number}?", "Remover", "Cancelar");
        if (!yes) return;

        var r = await _db.DeleteCommunityEpisodeAdminAsync(row.SeriesId, episode.Id, _session.IdToken);
        await DisplayAlert("Comunidade", r.ok ? "Episodio removido." : r.message, "OK");
        await LoadCommunityAsync();
    }

    private async void OnDeleteCommunitySeriesClicked(object sender, EventArgs e)
    {
        if (sender is not Button b || b.CommandParameter is not CommunityRowVm row) return;

        var yes = await DisplayAlert("Comunidade", $"Excluir serie \"{row.MainText}\" inteira?", "Excluir", "Cancelar");
        if (!yes) return;

        var r = await _db.DeleteCommunitySeriesAdminAsync(row.SeriesId, _session.IdToken);
        await DisplayAlert("Comunidade", r.ok ? "Serie excluida." : r.message, "OK");
        await LoadCommunityAsync();
    }

    private async void OnAddDramaClicked(object sender, EventArgs e)
    {
        var title = await DisplayPromptAsync("Novo drama", "Titulo:");
        if (string.IsNullOrWhiteSpace(title)) return;

        var subtitle = await DisplayPromptAsync("Novo drama", "Subtitulo (opcional):") ?? "";
        var cover = await DisplayPromptAsync("Novo drama", "Cover URL (opcional):") ?? "";
        var poster = await DisplayPromptAsync("Novo drama", "Poster URL (opcional):") ?? "";
        var catsRaw = await DisplayPromptAsync("Novo drama", "Categorias separadas por virgula:") ?? "";
        var featured = await DisplayAlert("Novo drama", "Marcar como destaque?", "Sim", "Nao");
        var isVip = await DisplayAlert("Novo drama", "Marcar como VIP?", "Sim", "Nao");

        var topStr = await DisplayPromptAsync("Novo drama", "Top rank (0 para ignorar):", initialValue: "0");
        _ = int.TryParse(topStr, out var topRank);

        var categories = catsRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        var drama = new DramaSeries
        {
            Title = title.Trim(),
            Subtitle = subtitle.Trim(),
            CoverUrl = cover.Trim(),
            PosterUrl = poster.Trim(),
            Categories = categories,
            IsFeatured = featured,
            IsVip = isVip,
            TopRank = Math.Max(0, topRank)
        };

        var r = await _db.UpsertDramaSeriesAsync(drama, _session.IdToken);
        await DisplayAlert("Discover", r.ok ? $"Drama salvo. ID: {r.dramaId}" : r.message, "OK");
        await LoadDramasAsync();
    }

    private async void OnAddDramaEpisodeClicked(object sender, EventArgs e)
    {
        if (sender is not Button b || b.CommandParameter is not DramaRowVm row) return;

        var numStr = await DisplayPromptAsync("Adicionar episodio", $"Numero do episodio para \"{row.MainText}\" (0 para auto):", initialValue: "0");
        _ = int.TryParse(numStr, out var number);

        var title = await DisplayPromptAsync("Adicionar episodio", "Titulo:");
        if (string.IsNullOrWhiteSpace(title)) return;

        var video = await DisplayPromptAsync("Adicionar episodio", "Video URL (.mp4):");
        if (string.IsNullOrWhiteSpace(video)) return;

        var thumb = await DisplayPromptAsync("Adicionar episodio", "Thumb URL (opcional):") ?? "";

        var durStr = await DisplayPromptAsync("Adicionar episodio", "Duracao em segundos (opcional):", initialValue: "0");
        _ = int.TryParse(durStr, out var duration);

        var episodeId = Guid.NewGuid().ToString("N");

        var subtitleSelection = await ResolveDramaSubtitleAsync(
            dramaId: row.DramaId,
            episodeId: episodeId,
            currentSubtitleUrl: "",
            currentSubtitleFormat: "",
            allowRemove: false
        );

        if (!subtitleSelection.proceed)
            return;

        var ep = new DramaEpisode
        {
            Id = episodeId,
            Number = Math.Max(0, number),
            Title = title.Trim(),
            VideoUrl = video.Trim(),
            ThumbUrl = thumb.Trim(),
            SubtitleUrl = subtitleSelection.subtitleUrl,
            SubtitleFormat = subtitleSelection.subtitleFormat,
            DurationSec = Math.Max(0, duration)
        };

        var r = await _db.UpsertDramaEpisodeAsync(row.DramaId, ep, _session.IdToken);
        await DisplayAlert("Discover", r.ok ? $"Episodio salvo. ID: {r.episodeId}" : r.message, "OK");
        await LoadDramasAsync();
    }

    private async void OnUpdateDramaSubtitleClicked(object sender, EventArgs e)
    {
        if (sender is not Button b || b.CommandParameter is not DramaRowVm row) return;

        var numStr = await DisplayPromptAsync("Legenda do episodio", $"Numero do episodio em \"{row.MainText}\":");
        if (string.IsNullOrWhiteSpace(numStr)) return;

        if (!int.TryParse(numStr.Trim(), out var number) || number <= 0)
        {
            await DisplayAlert("Discover", "Numero invalido.", "OK");
            return;
        }

        var episodes = await _db.GetEpisodesAsync(row.DramaId, _session.IdToken);
        var episode = episodes.FirstOrDefault(x => x.Number == number);
        if (episode == null || string.IsNullOrWhiteSpace(episode.Id))
        {
            await DisplayAlert("Discover", "Episodio nao encontrado.", "OK");
            return;
        }

        var subtitleSelection = await ResolveDramaSubtitleAsync(
            dramaId: row.DramaId,
            episodeId: episode.Id,
            currentSubtitleUrl: episode.SubtitleUrl,
            currentSubtitleFormat: episode.SubtitleFormat,
            allowRemove: true
        );

        if (!subtitleSelection.proceed)
            return;

        episode.SubtitleUrl = subtitleSelection.subtitleUrl;
        episode.SubtitleFormat = subtitleSelection.subtitleFormat;

        var r = await _db.UpsertDramaEpisodeAsync(row.DramaId, episode, _session.IdToken);
        await DisplayAlert("Discover", r.ok ? "Legenda atualizada." : r.message, "OK");
        await LoadDramasAsync();
    }

    private async void OnRemoveDramaEpisodeClicked(object sender, EventArgs e)
    {
        if (sender is not Button b || b.CommandParameter is not DramaRowVm row) return;

        var numStr = await DisplayPromptAsync("Remover episodio", $"Numero do episodio em \"{row.MainText}\":");
        if (string.IsNullOrWhiteSpace(numStr)) return;

        if (!int.TryParse(numStr.Trim(), out var number) || number <= 0)
        {
            await DisplayAlert("Discover", "Numero invalido.", "OK");
            return;
        }

        var episodes = await _db.GetEpisodesAsync(row.DramaId, _session.IdToken);
        var ep = episodes.FirstOrDefault(x => x.Number == number);
        if (ep == null || string.IsNullOrWhiteSpace(ep.Id))
        {
            await DisplayAlert("Discover", "Episodio nao encontrado.", "OK");
            return;
        }

        var yes = await DisplayAlert("Discover", $"Remover episodio {number} de \"{row.MainText}\"?", "Remover", "Cancelar");
        if (!yes) return;

        var r = await _db.DeleteDramaEpisodeAsync(row.DramaId, ep.Id, _session.IdToken);
        await DisplayAlert("Discover", r.ok ? "Episodio removido." : r.message, "OK");
        await LoadDramasAsync();
    }

    private async void OnDeleteDramaClicked(object sender, EventArgs e)
    {
        if (sender is not Button b || b.CommandParameter is not DramaRowVm row) return;

        var yes = await DisplayAlert("Discover", $"Excluir drama \"{row.MainText}\" inteiro?", "Excluir", "Cancelar");
        if (!yes) return;

        var r = await _db.DeleteDramaSeriesAsync(row.DramaId, _session.IdToken);
        await DisplayAlert("Discover", r.ok ? "Drama excluido." : r.message, "OK");
        await LoadDramasAsync();
    }

    private async Task<(bool proceed, string subtitleUrl, string subtitleFormat)> ResolveDramaSubtitleAsync(
        string dramaId,
        string episodeId,
        string currentSubtitleUrl,
        string currentSubtitleFormat,
        bool allowRemove
    )
    {
        var cancelLabel = allowRemove ? "Manter atual" : "Pular";
        var options = new List<string> { "Selecionar arquivo", "Informar URL" };
        if (allowRemove)
            options.Add("Remover");

        var choice = await DisplayActionSheet(
            "Legenda do episodio",
            cancelLabel,
            null,
            options.ToArray()
        );

        if (string.Equals(choice, cancelLabel, StringComparison.Ordinal))
            return (true, currentSubtitleUrl ?? "", SubtitleTrackService.NormalizeFormat(currentSubtitleFormat));

        if (allowRemove && string.Equals(choice, "Remover", StringComparison.Ordinal))
            return (true, "", "");

        if (string.Equals(choice, "Informar URL", StringComparison.Ordinal))
        {
            var prompt = await DisplayPromptAsync(
                "Legenda do episodio",
                "Informe a URL da legenda (.vtt ou .json):",
                initialValue: currentSubtitleUrl ?? ""
            );

            if (prompt == null)
                return (false, currentSubtitleUrl ?? "", currentSubtitleFormat ?? "");

            var url = prompt.Trim();
            if (string.IsNullOrWhiteSpace(url))
                return (true, "", "");

            return (true, url, InferSubtitleFormat(url, currentSubtitleFormat));
        }

        if (string.Equals(choice, "Selecionar arquivo", StringComparison.Ordinal))
        {
            FileResult? pick = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Selecione a legenda (.vtt ou .json)"
            });

            if (pick == null)
                return (false, currentSubtitleUrl ?? "", currentSubtitleFormat ?? "");

            var extension = System.IO.Path.GetExtension(pick.FileName ?? "").ToLowerInvariant();
            if (extension != ".vtt" && extension != ".json")
            {
                await DisplayAlert("Legenda", "Formatos aceitos: .vtt ou .json.", "OK");
                return (false, currentSubtitleUrl ?? "", currentSubtitleFormat ?? "");
            }

            await using var stream = await pick.OpenReadAsync();

            var upload = await _st.UploadDramaEpisodeSubtitleAsync(
                stream: stream,
                dramaId: dramaId,
                episodeId: episodeId,
                extension: extension,
                idToken: _session.IdToken
            );

            if (!upload.ok)
            {
                await DisplayAlert("Legenda", upload.message, "OK");
                return (false, currentSubtitleUrl ?? "", currentSubtitleFormat ?? "");
            }

            return (true, upload.url, extension.TrimStart('.'));
        }

        return (true, currentSubtitleUrl ?? "", SubtitleTrackService.NormalizeFormat(currentSubtitleFormat));
    }

    private static string InferSubtitleFormat(string? source, string? fallback = "")
    {
        var detected = SubtitleTrackService.DetectFormatFromPath(source);
        if (!string.IsNullOrWhiteSpace(detected))
            return detected;

        return SubtitleTrackService.NormalizeFormat(fallback);
    }

    private sealed class PayoutRowVm
    {
        public string RequestId { get; set; } = "";
        public string MainText { get; set; } = "";
        public string SecondaryText { get; set; } = "";
        public string StatusText { get; set; } = "";
        public bool CanDecide { get; set; }
    }

    private sealed class UserRowVm
    {
        public string Uid { get; set; } = "";
        public string MainText { get; set; } = "";
        public string SubText { get; set; } = "";
        public string StatusText { get; set; } = "";
        public bool CanModerate { get; set; }
    }

    private sealed class CommunityRowVm
    {
        public string SeriesId { get; set; } = "";
        public string MainText { get; set; } = "";
        public string SubText { get; set; } = "";
    }

    private sealed class DramaRowVm
    {
        public string DramaId { get; set; } = "";
        public string MainText { get; set; } = "";
        public string SubText { get; set; } = "";
    }
}
