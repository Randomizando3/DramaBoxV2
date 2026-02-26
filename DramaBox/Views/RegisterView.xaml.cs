// RegisterView.xaml.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using DramaBox.Models;
using DramaBox.Services;

namespace DramaBox.Views;

[QueryProperty(nameof(Ref), "ref")]
public partial class RegisterView : ContentPage
{
    private readonly FirebaseAuthService _auth;
    private readonly FirebaseDatabaseService _db;
    private readonly SessionService _session;

    private bool _isPasswordVisible;

    // ===== AFILIADO (captura por query) =====
    private string _ref = "";
    public string Ref
    {
        get => _ref;
        set
        {
            _ref = value ?? "";
            TryPersistReferral(_ref);
        }
    }

    private const string PrefReferralKey = "dramabox_ref_pending";

    public RegisterView()
    {
        InitializeComponent();

        _auth = Resolve<FirebaseAuthService>() ?? new FirebaseAuthService(new HttpClient());
        _db = Resolve<FirebaseDatabaseService>() ?? new FirebaseDatabaseService(new HttpClient());
        _session = Resolve<SessionService>() ?? new SessionService();

        _isPasswordVisible = false;
        if (PasswordEntry != null)
            PasswordEntry.IsPassword = true;

        // Fallback: se já veio de algum lugar e ficou salvo
        var pending = Preferences.Default.Get(PrefReferralKey, "");
        if (!string.IsNullOrWhiteSpace(pending))
            _ref = pending.Trim();
    }

    private static T? Resolve<T>() where T : class
        => Application.Current?.Handler?.MauiContext?.Services?.GetService(typeof(T)) as T;

    private static void TryPersistReferral(string? value)
    {
        try
        {
            var v = (value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(v)) return;

            var extracted = ExtractRefCode(v);
            if (!string.IsNullOrWhiteSpace(extracted))
                Preferences.Default.Set(PrefReferralKey, extracted.Trim().ToUpperInvariant());
        }
        catch { }
    }

    private static string ExtractRefCode(string raw)
    {
        raw = (raw ?? "").Trim();
        if (raw.Length == 0) return "";

        if (Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            // ?ref=CODE
            var q = uri.Query ?? "";
            if (!string.IsNullOrWhiteSpace(q))
            {
                foreach (var part in q.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = part.Split('=', 2);
                    if (kv.Length == 2 && string.Equals(kv[0], "ref", StringComparison.OrdinalIgnoreCase))
                        return (Uri.UnescapeDataString(kv[1] ?? "") ?? "").Trim();
                }
            }

            // /CODE no final (se você usar URL amigável no futuro)
            var last = uri.Segments?.Length > 0 ? uri.Segments[^1] : "";
            last = (last ?? "").Trim('/').Trim();
            if (!string.IsNullOrWhiteSpace(last)) return last;

            return raw;
        }

        // se já veio “CODE” puro
        return raw;
    }

    private void ClearPendingReferral()
    {
        try
        {
            Preferences.Default.Remove(PrefReferralKey);
            _ref = "";
        }
        catch { }
    }

    private void OnTogglePasswordClicked(object sender, EventArgs e)
    {
        _isPasswordVisible = !_isPasswordVisible;
        if (PasswordEntry != null)
            PasswordEntry.IsPassword = !_isPasswordVisible;
    }

    private static long UnixNow() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static string NormalizeEmail(string? email)
        => (email ?? "").Trim().ToLowerInvariant();

    /// <summary>
    /// Quando NÃO existe pendingCode salvo (app abriu direto no register sem query/ref),
    /// fazemos um scan em affiliates/leads/* procurando o email pending.
    /// Regras abertas permitem isso, mas é pesado: use só como fallback.
    /// Retorna o CODE (bucket) se achar.
    /// </summary>
    private async System.Threading.Tasks.Task<string?> FindAffiliateCodeByEmailScanAsync(string emailLower, string idToken)
    {
        emailLower = NormalizeEmail(emailLower);
        if (string.IsNullOrWhiteSpace(emailLower)) return null;

        // Estrutura esperada:
        // affiliates/leads/{CODE}/{pushId} => { emailLower, status, createdAtUnix... }
        var all = await _db.GetAsync<Dictionary<string, Dictionary<string, object>>>("affiliates/leads", idToken);
        if (all == null || all.Count == 0) return null;

        // varre buckets (CODEs)
        foreach (var bucket in all)
        {
            var code = (bucket.Key ?? "").Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(code)) continue;

            // tenta ler o bucket tipado (bem mais seguro do que mexer em object)
            var leads = await _db.GetAsync<Dictionary<string, FirebaseDatabaseService.AffiliateLead>>($"affiliates/leads/{code}", idToken);
            if (leads == null || leads.Count == 0) continue;

            var match = leads
                .Select(kv =>
                {
                    var l = kv.Value ?? new FirebaseDatabaseService.AffiliateLead();
                    l.Id = kv.Key ?? "";
                    l.EmailLower = NormalizeEmail(l.EmailLower);
                    l.Status = (l.Status ?? "pending").Trim();
                    return l;
                })
                .Where(l =>
                    l.StatusEquals("pending") &&
                    string.Equals(l.EmailLower, emailLower, StringComparison.OrdinalIgnoreCase)
                )
                .OrderBy(l => l.CreatedAtUnix)
                .FirstOrDefault();

            if (match != null && !string.IsNullOrWhiteSpace(match.Id))
                return code;
        }

        return null;
    }

    private async void OnRegisterClicked(object sender, EventArgs e)
    {
        var name = NameEntry?.Text?.Trim() ?? "";
        var email = EmailEntry?.Text?.Trim() ?? "";
        var password = PasswordEntry?.Text ?? "";

        if (string.IsNullOrWhiteSpace(name) ||
            string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(password))
        {
            await DisplayAlert("Cadastro", "Preencha nome, email e senha.", "OK");
            return;
        }

        try
        {
            // 1) tenta pegar o código salvo via query (?ref=CODE) ou persistência
            var pendingCode = (Preferences.Default.Get(PrefReferralKey, "") ?? "").Trim().ToUpperInvariant();

            // 2) cria usuário no Auth
            var (ok, message, result) = await _auth.SignUpAsync(email, password);
            if (!ok || result == null)
            {
                await DisplayAlert("Cadastro", message, "OK");
                return;
            }

            // 3) cria sessão
            _session.SetSession(result.IdToken, result.RefreshToken, result.LocalId, result.Email);

            // 4) salva perfil
            var profile = new UserProfile
            {
                UserId = result.LocalId,
                Email = result.Email,
                Nome = name,
                FotoUrl = "",
                Plano = "free"
            };

            var (saved, saveMsg) = await _db.UpsertUserProfileAsync(result.LocalId, profile, result.IdToken);
            if (!saved)
            {
                await DisplayAlert("Cadastro", saveMsg, "OK");
                return;
            }

            // 5) AFILIADO: confirma
            //    - primeiro com pendingCode (fluxo normal)
            //    - se não tiver pendingCode, faz scan por email (fallback)
            var emailLower = NormalizeEmail(result.Email ?? email);
            string? codeToUse = !string.IsNullOrWhiteSpace(pendingCode) ? pendingCode : null;

            if (string.IsNullOrWhiteSpace(codeToUse))
                codeToUse = await FindAffiliateCodeByEmailScanAsync(emailLower, result.IdToken);

            if (!string.IsNullOrWhiteSpace(codeToUse))
            {
                var (cok, cmsg) = await _db.ConfirmAffiliateLeadOnRegisterByCodeAsync(
                    affiliateCode: codeToUse,
                    registeredEmail: emailLower,
                    newUserUid: result.LocalId,
                    idToken: result.IdToken
                );

                // debug sempre
                var now = UnixNow();
                await _db.PatchAsync("", new Dictionary<string, object?>
                {
                    [$"users/{result.LocalId}/affiliate/confirmOk"] = cok,
                    [$"users/{result.LocalId}/affiliate/confirmMsg"] = cmsg,
                    [$"users/{result.LocalId}/affiliate/source"] = string.IsNullOrWhiteSpace(pendingCode) ? "email-scan" : "pagina.php",
                    [$"users/{result.LocalId}/affiliate/debugCode"] = codeToUse,
                    [$"users/{result.LocalId}/affiliate/debugAtUnix"] = now,
                }, result.IdToken);

                ClearPendingReferral();

                // se falhou, mostra (agora você enxerga o motivo imediatamente)
                if (!cok)
                    await DisplayAlert("Afiliado", $"Não consegui confirmar: {cmsg}", "OK");
            }

            // 6) finaliza
            _session.SetProfile(profile);

            Application.Current!.MainPage = new AppShell();
            await Shell.Current.GoToAsync("//discover");
        }
        catch (Exception ex)
        {
            // debug opcional (pra você ver no device)
            try
            {
                await DisplayAlert("Cadastro", $"Erro inesperado ao criar conta.\n{ex.Message}", "OK");
            }
            catch
            {
                await DisplayAlert("Cadastro", "Erro inesperado ao criar conta.", "OK");
            }
        }
    }

    private async void OnBackToLoginClicked(object sender, EventArgs e)
        => await Navigation.PopAsync();
}