using System.Collections.Generic;

namespace DramaBox.Models;

public sealed class CommunitySeries
{
    public string Id { get; set; } = "";

    public string CreatorUserId { get; set; } = "";
    public string CreatorName { get; set; } = "";
    public bool CreatorIsVip { get; set; }

    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";

    public string CoverUrl { get; set; } = "";
    public string PosterUrl { get; set; } = "";

    public bool IsPublished { get; set; } = true;

    public List<string> Tags { get; set; } = new();

    public long CreatedAtUnix { get; set; }
    public long UpdatedAtUnix { get; set; }
}
