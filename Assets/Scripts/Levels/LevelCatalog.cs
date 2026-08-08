using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ScrapSiege.Monetization;

namespace ScrapSiege.Levels
{
    /// <summary>
    /// The list of shipped levels, plus the one-slot handoff of "which level did the player pick"
    /// between the menu scene and the match scene.
    ///
    /// A static selection rather than a serialized singleton: it survives the scene load without
    /// needing DontDestroyOnLoad on a manager object, and there is exactly one match in flight at a
    /// time so there is nothing to keep per-instance.
    /// </summary>
    [CreateAssetMenu(fileName = "LevelCatalog", menuName = "Scrap Siege/Level Catalog")]
    public class LevelCatalog : ScriptableObject
    {
        [SerializeField] private LevelDefinition[] levels = new LevelDefinition[0];

        /// <summary>All levels in author order, nulls stripped so one empty slot can't break the menu.</summary>
        public IReadOnlyList<LevelDefinition> Levels =>
            levels.Where(l => l != null).OrderBy(l => l.levelNumber).ToList();

        /// <summary>The level the player chose in the menu. Null means "fall back to the default".</summary>
        public static LevelDefinition Selected { get; private set; }

        public static void Select(LevelDefinition level)
        {
            Selected = level;
            Debug.Log($"LevelCatalog: selected '{(level != null ? level.displayName : "<null>")}'.");
        }

        /// <summary>
        /// Whether the player can currently play this level. Pro levels read the same decoupled
        /// entitlement gate gameplay code already uses, never the RevenueCat SDK directly.
        /// </summary>
        public static bool IsUnlocked(LevelDefinition level)
        {
            if (level == null) return false;
            return !level.requiresPro || ProEntitlement.IsUnlocked;
        }

        /// <summary>First playable level - used when the match scene is entered without a selection (e.g. straight from the Editor).</summary>
        public LevelDefinition FirstOrDefaultLevel()
        {
            var ordered = Levels;
            return ordered.Count > 0 ? ordered[0] : null;
        }
    }
}
