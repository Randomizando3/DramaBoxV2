namespace DramaBox.Models;

public sealed class CommunityRoyaltyConfig
{
    // valores em "centavos" (double) conforme seu exemplo
    public double PerLike { get; set; } = 0.001;
    public double PerShare { get; set; } = 0.003;
    public double PerMinuteWatched { get; set; } = 0.01;

    public long UpdatedAtUnix { get; set; }
}
