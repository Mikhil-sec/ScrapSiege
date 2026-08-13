using System.Collections.Generic;
using UnityEngine;
using ScrapSiege.Monetization;

namespace ScrapSiege.Siege
{
    /// <summary>
    /// The ordered list of unit classes the player can deploy, and the AI's weighted pick list.
    ///
    /// <para>Mirrors <see cref="ScrapSiege.Levels.LevelCatalog"/> deliberately: one asset lists the
    /// content, one static helper answers "is this unlocked", and the UI is generated from it. That
    /// keeps "ship a new unit" down to an asset plus a roster entry.</para>
    /// </summary>
    [CreateAssetMenu(fileName = "UnitRoster", menuName = "Scrap Siege/Unit Roster")]
    public class UnitRoster : ScriptableObject
    {
        [Tooltip("Every deployable class, in roster order. The first UNLOCKED entry is what a match " +
                 "starts with selected.")]
        [SerializeField] private List<UnitClass> classes = new List<UnitClass>();

        [Header("AI commander")]
        [Tooltip("Classes the AI is allowed to field, and how likely each is. Kept separate from the " +
                 "player list so a Pro-gated class can never leak into the AI's roster and make the " +
                 "opponent stronger for players who have not paid - which would be a genuinely ugly " +
                 "way to sell a subscription.")]
        [SerializeField] private List<WeightedUnitClass> aiPicks = new List<WeightedUnitClass>();

        public IReadOnlyList<UnitClass> Classes => classes;

        /// <summary>
        /// Roster order, filtered to nothing - locked classes are deliberately INCLUDED so the
        /// player can see them. Callers check <see cref="IsUnlocked"/> per entry.
        /// </summary>
        public IEnumerable<UnitClass> Ordered
        {
            get
            {
                var sorted = new List<UnitClass>(classes);
                sorted.RemoveAll(c => c == null);
                sorted.Sort((a, b) => a.rosterOrder.CompareTo(b.rosterOrder));
                return sorted;
            }
        }

        /// <summary>
        /// Routes through <see cref="ProEntitlement"/> rather than the RevenueCat SDK, exactly like
        /// LevelCatalog does - the SDK is touched in one place in this project and this is not it.
        /// </summary>
        public static bool IsUnlocked(UnitClass unitClass)
        {
            if (unitClass == null) return false;
            return !unitClass.requiresPro || ProEntitlement.IsUnlocked;
        }

        /// <summary>The class a match should open with - the cheapest unlocked entry in roster order.</summary>
        public UnitClass DefaultClass()
        {
            foreach (var candidate in Ordered)
                if (IsUnlocked(candidate)) return candidate;

            return null;
        }

        /// <summary>
        /// Weighted pick for the AI. Returns null only if the roster has no AI entries at all, which
        /// the commander treats as "fall back to the serialized prefab's own class".
        /// </summary>
        public UnitClass PickForAI(int scrapAvailable)
        {
            float total = 0f;
            foreach (var entry in aiPicks)
            {
                if (entry.unitClass == null) continue;
                if (entry.unitClass.cost > scrapAvailable) continue;
                total += Mathf.Max(0f, entry.weight);
            }

            if (total <= 0f) return null;

            float roll = Random.value * total;
            foreach (var entry in aiPicks)
            {
                if (entry.unitClass == null) continue;
                if (entry.unitClass.cost > scrapAvailable) continue;

                roll -= Mathf.Max(0f, entry.weight);
                if (roll <= 0f) return entry.unitClass;
            }

            return null;
        }

        /// <summary>The cheapest thing the AI could ever field, so it knows when to keep banking.</summary>
        public int CheapestAICost()
        {
            int cheapest = int.MaxValue;
            foreach (var entry in aiPicks)
                if (entry.unitClass != null) cheapest = Mathf.Min(cheapest, entry.unitClass.cost);

            return cheapest == int.MaxValue ? 1 : cheapest;
        }
    }

    [System.Serializable]
    public struct WeightedUnitClass
    {
        public UnitClass unitClass;

        [Tooltip("Relative likelihood. Weights need not sum to anything in particular.")]
        public float weight;
    }
}
