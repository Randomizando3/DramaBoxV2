namespace DramaBox.Models;

public sealed class CommunitySeriesMetrics
{
    public double Likes { get; set; }
    public double Shares { get; set; }
    public double MinutesWatched { get; set; }
    public long UpdatedAtUnix { get; set; }
}
