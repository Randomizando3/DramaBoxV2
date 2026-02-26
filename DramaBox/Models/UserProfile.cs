// Models/UserProfile.cs
using System;

namespace DramaBox.Models;

public sealed class UserProfile
{
    // ===== Identidade =====
    public string UserId { get; set; } = "";     // localId do Firebase Auth
    public string Email { get; set; } = "";

    // ===== Perfil (padrão "direto") =====
    public string Nome { get; set; } = "";       // <- usado nas Views
    public string FotoUrl { get; set; } = "";    // <- usado nas Views (url pública/baixável)
    public string Plano { get; set; } = "free";  // <- "free" | "premium"

    // ===== Premium (VIP) =====
    // 0 = sem prazo (infinito). >0 = unix seconds de vencimento
    public long PremiumUntilUnix { get; set; } = 0;

    // ===== Compat =====
    public string DisplayName
    {
        get => Nome ?? "";
        set => Nome = value ?? "";
    }

    public string PhotoUrl
    {
        get => FotoUrl ?? "";
        set => FotoUrl = value ?? "";
    }

    public bool IsPremium
    {
        get
        {
            var isPlanPremium = string.Equals(Plano, "premium", StringComparison.OrdinalIgnoreCase);
            if (!isPlanPremium) return false;

            if (PremiumUntilUnix <= 0) return true; // sem prazo = premium ativo
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return PremiumUntilUnix > now;
        }
        set
        {
            Plano = value ? "premium" : "free";
            if (!value) PremiumUntilUnix = 0;
        }
    }

    // ===== Auditoria =====
    public long CreatedAtUnix { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}