// Views/RegisterView.xaml.cs
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

        if (PasswordEntry != null) PasswordEntry.IsPassword = true;
        if (ConfirmPasswordEntry != null) ConfirmPasswordEntry.IsPassword = true;

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

            // /CODE no final
            var last = uri.Segments?.Length > 0 ? uri.Segments[^1] : "";
            last = (last ?? "").Trim('/').Trim();
            if (!string.IsNullOrWhiteSpace(last)) return last;

            return raw;
        }

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

        if (ConfirmPasswordEntry != null)
            ConfirmPasswordEntry.IsPassword = !_isPasswordVisible;
    }

    private static long UnixNow() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static string NormalizeEmail(string? email)
        => (email ?? "").Trim().ToLowerInvariant();

    private static string NormalizePhone(string? phone)
        => new string((phone ?? "").Where(char.IsDigit).ToArray());

    /// <summary>
    /// Quando NÃO existe pendingCode salvo, fazemos scan em affiliates/leads/* procurando o email pending.
    /// Retorna o CODE (bucket) se achar.
    /// </summary>
    private async System.Threading.Tasks.Task<string?> FindAffiliateCodeByEmailScanAsync(string emailLower, string idToken)
    {
        emailLower = NormalizeEmail(emailLower);
        if (string.IsNullOrWhiteSpace(emailLower)) return null;

        var all = await _db.GetAsync<Dictionary<string, Dictionary<string, object>>>("affiliates/leads", idToken);
        if (all == null || all.Count == 0) return null;

        foreach (var bucket in all)
        {
            var code = (bucket.Key ?? "").Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(code)) continue;

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

        var phoneRaw = PhoneEntry?.Text?.Trim() ?? "";
        var phoneDigits = NormalizePhone(phoneRaw);

        var email = EmailEntry?.Text?.Trim() ?? "";
        var email2 = ConfirmEmailEntry?.Text?.Trim() ?? "";

        var password = PasswordEntry?.Text ?? "";
        var password2 = ConfirmPasswordEntry?.Text ?? "";

        if (string.IsNullOrWhiteSpace(name) ||
            string.IsNullOrWhiteSpace(phoneDigits) ||
            string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(email2) ||
            string.IsNullOrWhiteSpace(password) ||
            string.IsNullOrWhiteSpace(password2))
        {
            await DisplayAlert("Cadastro", "Preencha nome, telefone, email (2x) e senha (2x).", "OK");
            return;
        }

        if (!string.Equals(NormalizeEmail(email), NormalizeEmail(email2), StringComparison.OrdinalIgnoreCase))
        {
            await DisplayAlert("Cadastro", "Os emails não conferem.", "OK");
            return;
        }

        if (!string.Equals(password, password2, StringComparison.Ordinal))
        {
            await DisplayAlert("Cadastro", "As senhas não conferem.", "OK");
            return;
        }

        // validação simples de telefone (ajuste se quiser)
        if (phoneDigits.Length < 10)
        {
            await DisplayAlert("Cadastro", "Telefone inválido. Informe com DDD.", "OK");
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

            // 4) salva perfil (inclui telefone)
            var profile = new UserProfile
            {
                UserId = result.LocalId,
                Email = result.Email,
                Nome = name,
                FotoUrl = "",
                Plano = "free",
                Telefone = phoneDigits // <<<<< NOVO
            };

            var (saved, saveMsg) = await _db.UpsertUserProfileAsync(result.LocalId, profile, result.IdToken);
            if (!saved)
            {
                await DisplayAlert("Cadastro", saveMsg, "OK");
                return;
            }

            // 5) AFILIADO: confirma (pendingCode normal, scan fallback)
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