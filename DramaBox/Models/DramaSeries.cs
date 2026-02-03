namespace DramaBox.Models;

public sealed class DramaSeries
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string CoverUrl { get; set; } = "";
    public string PosterUrl { get; set; } = ""; // opcional (pode repetir cover)
    public string[] Categories { get; set; } = Array.Empty<string>();

    // Destaques / organização
    public bool IsFeatured { get; set; } = false;     // aparece no slider topo
    public bool IsVip { get; set; } = false;          // trava (premium)
    public int TopRank { get; set; } = 0;             // Top 10 (1..10)
    public long UpdatedAtUnix { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    // ====== UI STATE (não precisa ir pro Firebase) ======
    public bool UiLiked { get; set; }
    public bool UiInPlaylist { get; set; }
}
