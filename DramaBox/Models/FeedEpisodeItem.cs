// Models/FeedEpisodeItem.cs
namespace DramaBox.Models;

public sealed class FeedEpisodeItem
{
    // drama
    public string DramaId { get; set; } = "";
    public string DramaTitle { get; set; } = "";
    public string DramaSubtitle { get; set; } = "";
    public string CoverUrl { get; set; } = "";

    // criador
    public string CreatorUserId { get; set; } = "";
    public string CreatorName { get; set; } = "";

    // episódio
    public string EpisodeId { get; set; } = "";
    public int EpisodeNumber { get; set; }
    public string EpisodeTitle { get; set; } = "";
    public string VideoUrl { get; set; } = ""; // mp4

    // opcional: se quiser filtrar VIP
    public bool IsVip { get; set; }
}
