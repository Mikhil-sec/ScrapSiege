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
            if (agent == null) return;

            agent.areaMask = NavMesh.AllAreas;
            agent.SetAreaCost(CoverAreaIndex, preferCover ? CoverAreaCost : NeutralAreaCost);
        }
    }
}
