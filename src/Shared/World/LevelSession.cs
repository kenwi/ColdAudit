namespace ColdAudit.Shared.World;

public sealed class LevelSession
{
    public int LevelNumber { get; set; } = 1;
    public string LevelId { get; set; } = "1";
    public bool IsLoaded { get; set; }
}
