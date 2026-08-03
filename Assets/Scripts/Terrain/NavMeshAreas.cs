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
        /// Low relative to the default area cost of 1 - cheap enough that a NavMeshAgent whose
        /// areaMask includes this area will detour through it for partial-length savings, giving
        /// the "Covered" deploy route its characteristic hug-the-terrain path. Tuned aggressively
        /// low (rather than a gentler discount) because on a small tabletop the possible detour
        /// distances are short, so a weak discount was getting outweighed by even a small extra
        /// distance and produced a route indistinguishable from Direct.
        /// </summary>
        public const float CoverAreaCost = 0.08f;

        public static void ApplyGlobalCost()
        {
            NavMesh.SetAreaCost(CoverAreaIndex, CoverAreaCost);
        }
    }
}
