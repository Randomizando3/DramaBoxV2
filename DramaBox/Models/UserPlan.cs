// Models/UserPlan.cs
namespace DramaBox.Models;

public enum UserPlan
{
    Free = 0,
    Premium = 1
}


/// <summary>
/// Centraliza infos do plano (preço, labels, etc).
/// </summary>
public static class UserPlanInfo
{
    // Valor do plano Premium (exemplo)
    public const decimal PremiumPrice = 29.90m;

    // Label já formatada pra UI (pt-BR)
    public const string PremiumPriceLabel = "R$ 29,90/mês";
}