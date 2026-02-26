// Models/AffiliateRoyaltyConfig.cs
namespace DramaBox.Models;

public sealed class AffiliateRoyaltyConfig
{
    // quantas moedas ganha por cadastro confirmado (lead pending -> confirmed)
    public int CoinsPerConfirmedSignup { get; set; } = 50;

    // (placeholder) valor em reais por confirmado, pra você usar depois
    public double ReaisPerConfirmedSignup { get; set; } = 0.0;

    public long UpdatedAtUnix { get; set; }
}