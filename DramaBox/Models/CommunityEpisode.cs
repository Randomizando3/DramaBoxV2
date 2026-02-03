namespace DramaBox.Models;

public sealed class CommunityEpisode
{
    public string Id { get; set; } = "";
    public int Number { get; set; }
    public string Title { get; set; } = "";

    // DEVE ser mp4
    public string VideoUrl { get; set; } = "";

    public int DurationSeconds { get; set; }
    public long CreatedAtUnix { get; set; }
}
