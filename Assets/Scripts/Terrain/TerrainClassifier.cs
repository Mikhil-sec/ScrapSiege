using System.Collections.Generic;

namespace ScrapSiege.Terrain
{
    /// <summary>
    /// Deterministic, rule-based geometry classification - no ML, per plan.md's
    /// "No AI, by design" section. Same measured shape always maps to the same archetype.
    /// </summary>
    public static class TerrainClassifier
    {
        // Tuned via playtesting, per plan.md. Starting values - adjust once objects are tested on a real table.
        private const float ElongatedAspectRatioThreshold = 2.2f;
        private const float CoverMinFootprintArea = 0.03f; // ~17cm x 17cm or larger

        public static TerrainArchetype Classify(TerrainObjectData data)
        {
            if (data.AspectRatio >= ElongatedAspectRatioThreshold)
                return TerrainArchetype.WallBarricade;

            if (data.Height == HeightCategory.Tall)
                return TerrainArchetype.SpireChokepoint;

            if (data.Height == HeightCategory.Short && data.FootprintArea >= CoverMinFootprintArea)
                return TerrainArchetype.RubbleCover;

            return TerrainArchetype.PlainObstacle;
        }

        /// <summary>
        /// Post-pass after all objects on the board are scanned: the single tallest object
        /// becomes the Watchtower bonus tier (plan.md Mechanic 1 table), overriding its
        /// otherwise-assigned archetype.
        /// </summary>
        public static void ApplyWatchtowerOverride(List<TerrainObjectData> objects)
        {
            TerrainObjectData tallest = null;
            foreach (var obj in objects)
            {
                if (obj.Height != HeightCategory.Tall) continue;
                if (tallest == null || obj.FootprintArea > tallest.FootprintArea)
                    tallest = obj;
            }

            if (tallest != null)
                tallest.Archetype = TerrainArchetype.Watchtower;
        }
    }
}
