using System;
using UnityEngine;
using ScrapSiege.Terrain;

namespace ScrapSiege.Levels
{
    /// <summary>
    /// One hand-authored battlefield, stored in **normalised board space** so the same layout
    /// projects onto a coffee table or a dining table at whatever size the player chose.
    ///
    /// Normalised space: x and z both run 0..1 across the board footprint. The board's real world
    /// size is decided at placement time; LevelBuilder maps these coordinates onto it. Authoring in
    /// metres instead would bake in one table size and break on every other surface.
    ///
    /// Convention: z = 0 is the player's near edge (where they stand and deploy), z = 1 is the
    /// enemy's end. Keeping that fixed means "advance" always means +z and level authoring stays
    /// readable.
    /// </summary>
    [CreateAssetMenu(fileName = "Level", menuName = "Scrap Siege/Level Definition")]
    public class LevelDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string displayName = "Untitled";

        [Tooltip("Shown on the level card. One line on what makes this map different to play.")]
        [TextArea(2, 4)]
        public string briefing;

        [Tooltip("Order in the level select list.")]
        public int levelNumber = 1;

        [Tooltip("Gate this level behind the RevenueCat 'pro' entitlement (plan.md Section 7 level packs).")]
        public bool requiresPro;

        [Header("Board shape")]
        [Tooltip("Board width as a fraction of its length. 0.6 gives a rectangle that reads well " +
                 "across a table without the far end being out of comfortable reach.")]
        [Range(0.3f, 1f)]
        public float boardAspect = 0.6f;

        [Tooltip("Multiplies every terrain piece's height on this map. 1 is the standard table " +
                 "(Short/Medium/Tall = 5.5% / 13% / 22% of board length). Raise it for a map built " +
                 "around not being able to see across the board - which is the AR mechanic's whole " +
                 "point, and something a level should be able to lean on harder than the default. " +
                 "Height affects sight and silhouette ONLY; terrain carves the NavMesh by footprint, " +
                 "so raising this cannot sever a route.")]
        [Range(0.5f, 2.5f)]
        public float terrainHeightScale = 1f;

        [Header("Layout (normalised 0..1, z=0 is the player's edge)")]
        public TerrainPlacement[] terrain = Array.Empty<TerrainPlacement>();

        public Vector2 playerBasePosition = new Vector2(0.5f, 0.08f);
        public Vector2 enemyBasePosition = new Vector2(0.5f, 0.92f);

        [Header("Balance")]
        public int startingResources = 3;
        public int playerBaseHealth = 50;
        public int enemyBaseHealth = 50;

        [Tooltip("Chokepoint/Watchtower terrain spawns a free defending sentry, capped here.")]
        public int maxGarrisonUnits = 3;

        [Header("AI commander")]
        [Tooltip("Whether this level is played against an AI commander that deploys its own units - " +
                 "and therefore whether the player can LOSE here. Off for the three original levels, " +
                 "which are authored for a one-directional siege and are balanced around it. Gating " +
                 "per level is what makes it safe to add the AI without re-tuning everything else.")]
        public bool hasAICommander;

        [Tooltip("Difficulty tier for this level's commander. Ignored unless Has AI Commander is on.")]
        public ScrapSiege.Siege.AICommanderProfile aiProfile;

        [Header("Star thresholds")]
        [Tooltip("Win within this many seconds for the time star.")]
        public float parTimeSeconds = 120f;

        [Tooltip("Lose no more than this many units for the efficiency star.")]
        public int parUnitsLost = 5;

        /// <summary>
        /// Stars earned for a WON match: one for winning at all, one for beating
        /// <see cref="parTimeSeconds"/>, one for beating <see cref="parUnitsLost"/>.
        ///
        /// <para>Both pars have been authored on every level since levels existed and were read by
        /// absolutely nothing until 2026-08-14 - the rating is the cheapest content the project has,
        /// because the design work was already done and only the arithmetic was missing. Winning is
        /// worth a star on its own so a rating can never read as a failure; the other two are the
        /// two axes the game actually has, speed and attrition.</para>
        ///
        /// <para>A par of zero or less means "this level does not grade that axis", and awards the
        /// star rather than withholding it - an unauthored threshold must not silently make a level
        /// unrateable.</para>
        /// </summary>
        public int StarsFor(float seconds, int unitsLost)
        {
            int stars = 1;
            if (parTimeSeconds <= 0f || seconds <= parTimeSeconds) stars++;
            if (parUnitsLost <= 0 || unitsLost <= parUnitsLost) stars++;
            return stars;
        }

        /// <summary>
        /// Converts a normalised board coordinate into a position in the board root's local space,
        /// where the board is 1.0 long on z and <see cref="boardAspect"/> wide on x, centred on the
        /// root. Keeping this on the asset means every consumer maps coordinates identically.
        /// </summary>
        public Vector3 ToBoardLocal(Vector2 normalised)
        {
            return new Vector3(
                (normalised.x - 0.5f) * boardAspect,
                0f,
                normalised.y - 0.5f);
        }

        private void OnValidate()
        {
            // Catch authoring mistakes in the Inspector rather than as a mysteriously empty board.
            if (terrain == null) return;

            for (int i = 0; i < terrain.Length; i++)
            {
                var t = terrain[i];
                if (t.position.x < 0f || t.position.x > 1f || t.position.y < 0f || t.position.y > 1f)
                    Debug.LogWarning($"{name}: terrain[{i}] position {t.position} is outside the 0..1 board - it will spawn off the board.", this);

                if (t.size.x <= 0f || t.size.y <= 0f)
                    Debug.LogWarning($"{name}: terrain[{i}] has a zero/negative size and will be invisible.", this);
            }
        }
    }

    /// <summary>One authored terrain piece, in normalised board space.</summary>
    [Serializable]
    public struct TerrainPlacement
    {
        public TerrainArchetype archetype;

        [Tooltip("Normalised board position. x across, y along (y=0 is the player's edge).")]
        public Vector2 position;

        [Tooltip("Normalised footprint. x across, y along.")]
        public Vector2 size;

        [Tooltip("Yaw in degrees about the board's up axis.")]
        public float rotationDegrees;

        public HeightCategory height;
    }
}
