namespace ScrapSiege.Terrain
{
    /// <summary>
    /// Gameplay terrain buckets from plan.md Mechanic 1's classification table.
    /// Assigned by rule-based geometry only - never inferred from what the real object is.
    /// </summary>
    public enum TerrainArchetype
    {
        PlainObstacle,
        RubbleCover,
        SpireChokepoint,
        WallBarricade,
        Watchtower
    }

    /// <summary>
    /// Tier B (no depth sensor) height input: the player picks a size category with one tap
    /// instead of the app measuring height automatically (Tier A).
    /// </summary>
    public enum HeightCategory
    {
        Short,
        Medium,
        Tall
    }
}
