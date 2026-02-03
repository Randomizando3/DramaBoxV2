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

    // ===== Compat (se você já tinha isso em outras partes) =====
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

    // Para facilitar em lógica futura
    public bool IsPremium
    {
        get => string.Equals(Plano, "premium", StringComparison.OrdinalIgnoreCase);
        set => Plano = value ? "premium" : "free";
    }

    // ===== Auditoria =====
    public long CreatedAtUnix { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
