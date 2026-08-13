using UnityEngine.AI;

namespace ScrapSiege.Terrain
{
    /// <summary>
    /// The route-variety mechanic (cover vs. speed trade-off) reuses one of Unity's 29
    /// user-definable NavMesh area slots (indices 3-31) rather than a custom-named one, so it
    /// works without any Navigation-window setup - see the "Unity Editor steps" note for the
    /// one optional cosmetic rename. TerrainObjectSpawner tags ground near RubbleCover/
    /// WallBarricade objects with this area; ScrapSiege.Siege consumes it for deploy routing
    /// and garrison detection.
    /// </summary>
    public static class NavMeshAreas
    {
        public const int CoverAreaIndex = 3;
        public const int CoverAreaMask = 1 << CoverAreaIndex;

        /// <summary>
        /// Low relative to the default area cost of 1 - cheap enough that a Covered-mode agent will
        /// detour through cover for partial-length savings, giving that route its characteristic
        /// hug-the-terrain path. Tuned aggressively low (rather than a gentler discount) because on
        /// a small tabletop the possible detour distances are short, so a weak discount was getting
        /// outweighed by even a small extra distance and produced a route indistinguishable
        /// from Direct.
        /// </summary>
        public const float CoverAreaCost = 0.08f;

        /// <summary>
        /// Neutral cost for Direct mode. Direct is NOT barred from cover - it simply has no reason
        /// to prefer it, so it takes the geometrically shortest line and will cut through a cover
        /// lane only when that genuinely is the shortest way. Barring it outright (the old
        /// approach) both contradicted the design and disconnected the board: on a narrow map the
        /// excluded cover polygons were the only link between the two halves, so Direct units had
        /// no complete path to the enemy base at all.
        /// </summary>
        public const float NeutralAreaCost = 1f;

        /// <summary>
        /// Baseline cost applied to the whole NavMesh. Per-agent overrides
        /// (<see cref="NavMeshAgent.SetAreaCost"/>) are what actually differentiate the two deploy
        /// routes - this just gives agents that never set one a sane default.
        /// </summary>
        public static void ApplyGlobalCost()
        {
            NavMesh.SetAreaCost(CoverAreaIndex, NeutralAreaCost);
        }

        /// <summary>
        /// Applies a deploy route's cover preference to one agent, without touching its areaMask.
        ///
        /// Area cost set through <see cref="NavMesh.SetAreaCost"/> is global to every agent, which
        /// is why routing used to be done by excluding the area from the agent's mask instead. But
        /// <see cref="NavMeshAgent.SetAreaCost"/> is a genuine per-agent override, so two agents can
        /// value the same ground differently on the same NavMesh - which is what this mechanic
        /// actually wants, and it keeps the whole board reachable for both.
        /// </summary>
        public static void ApplyCoverPreference(NavMeshAgent agent, bool preferCover)
        {
            ApplyCoverPreference(agent, preferCover, 1f);
        }

        /// <summary>
        /// As above, with a per-unit multiplier on the resulting cost.
        ///
        /// <para>Route variety's cheapest layer. The mode's base cost is the same number for every
        /// agent, so two units deployed the same way and heading the same place price every polygon
        /// identically and therefore receive the identical corner path - an army walking single
        /// file, which is what the 2026-08-13 device test reported. Scaling the cost slightly per
        /// unit makes them genuinely disagree about which side of an obstacle is worth it, so the
        /// spread comes out of real pathfinding rather than out of positional noise.</para>
        ///
        /// <para>This can only ever change how *attractive* cover is, never whether it is passable -
        /// the area mask is still <see cref="NavMesh.AllAreas"/> - so no amount of variance here can
        /// disconnect a map, which is the failure mode the old areaMask-exclusion approach had.</para>
        /// </summary>
        public static void ApplyCoverPreference(NavMeshAgent agent, bool preferCover, float costMultiplier)
        {
            if (agent == null) return;

            float baseCost = preferCover ? CoverAreaCost : NeutralAreaCost;

            agent.areaMask = NavMesh.AllAreas;
            // Unity treats an area cost below 1 as "cheaper than open ground" and rejects negatives;
            // the floor keeps a wild multiplier from producing a zero-cost polygon, which would make
            // every path collapse onto cover regardless of distance.
            agent.SetAreaCost(CoverAreaIndex, UnityEngine.Mathf.Max(0.01f, baseCost * costMultiplier));
        }
    }
}
