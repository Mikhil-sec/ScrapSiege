using UnityEngine;

namespace ScrapSiege.Levels
{
    /// <summary>
    /// Remembers the best star rating the player has earned on each level.
    ///
    /// <para><b>PlayerPrefs, deliberately.</b> This is three integers on a single-player game with
    /// no account system, no cloud save and nothing worth cheating for - the entire "save file" is
    /// smaller than the code that would be needed to write a real one. Anything more (a JSON file,
    /// a serialised save, a backend) would be scope this project cannot spend right now, and none of
    /// it would change what the player sees.</para>
    ///
    /// <para><b>It is not a security boundary and must never become one.</b> A player editing their
    /// own star count costs nobody anything. Nothing gated by
    /// <see cref="ScrapSiege.Monetization.ProEntitlement"/> is ever stored here - paid access is
    /// decided by RevenueCat, on RevenueCat's servers, and this file is not consulted about it.</para>
    /// </summary>
    public static class LevelProgress
    {
        // Namespaced so it cannot collide with any other PlayerPrefs key the project or a plugin
        // writes. Keyed on levelNumber rather than displayName so renaming a level for readability
        // does not silently wipe everyone's progress on it.
        private const string StarKeyPrefix = "scrapsiege.stars.";

        public static int BestStars(LevelDefinition level)
        {
            if (level == null) return 0;
            return Mathf.Clamp(PlayerPrefs.GetInt(StarKeyPrefix + level.levelNumber, 0), 0, 3);
        }

        /// <summary>
        /// Stores <paramref name="stars"/> if it beats what is already recorded. Returns true when
        /// this run was an improvement, so the outcome card can say so.
        /// </summary>
        public static bool RecordStars(LevelDefinition level, int stars)
        {
            if (level == null) return false;

            stars = Mathf.Clamp(stars, 0, 3);
            if (stars <= BestStars(level)) return false;

            PlayerPrefs.SetInt(StarKeyPrefix + level.levelNumber, stars);

            // Written immediately rather than left to the implicit save on quit: an AR app on
            // Android is routinely killed from the recents list rather than closed, and a result the
            // player watched themselves earn must not be the thing that gets lost to that.
            PlayerPrefs.Save();
            return true;
        }

        /// <summary>Filled and hollow stars, e.g. two of three as a compact glyph run for a label.</summary>
        public static string StarGlyphs(int stars)
        {
            stars = Mathf.Clamp(stars, 0, 3);
            return new string('★', stars) + new string('☆', 3 - stars);
        }

        /// <summary>Wipes every stored rating. Exposed for testing and for a future settings screen.</summary>
        public static void ClearAll(LevelCatalog catalog)
        {
            if (catalog == null) return;

            foreach (var level in catalog.Levels)
                if (level != null) PlayerPrefs.DeleteKey(StarKeyPrefix + level.levelNumber);

            PlayerPrefs.Save();
        }
    }
}
