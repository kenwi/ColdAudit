namespace ColdAudit.Shared.World;

public readonly record struct SectorId(string Value)
{
    public override string ToString() => Value;
}
