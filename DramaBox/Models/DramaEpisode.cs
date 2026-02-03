namespace DramaBox.Models;

public sealed class DramaEpisode
{
    public string Id { get; set; } = "";
    public int Number { get; set; } = 1;
    public string Title { get; set; } = "";
    public string VideoUrl { get; set; } = "";
    public string ThumbUrl { get; set; } = "";
    public int DurationSec { get; set; } = 0; // opcional
}
