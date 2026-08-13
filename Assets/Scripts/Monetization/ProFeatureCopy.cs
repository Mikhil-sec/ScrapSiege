using System.Collections.Generic;
using System.Text;
using ScrapSiege.Levels;
using ScrapSiege.Siege;

namespace ScrapSiege.Monetization
{
    /// <summary>
    /// The one place that answers "what does Scrap Siege Pro actually give you?".
    ///
    /// <para><b>Why this exists.</b> The paywall's feature list was static text authored directly
    /// into both scenes, and it went stale twice. The version shipped to the device on 2026-08-13
    /// still promised "more cosmetic board themes" and "extra visual effect packs" - two systems
    /// that were never built - while saying nothing about level 05, the Veteran AI tier or the
    /// Turret class, which are the three things a subscriber genuinely gets. Two scenes each
    /// holding their own copy of a promise about a third system is a guarantee of drift, and
    /// drifting *here* is worse than drifting anywhere else in the project: the player is being
    /// asked for real money against it.</para>
    ///
    /// <para><b>Why it is derived, not just centralised.</b> Moving the string into one C# constant
    /// would fix today's drift and not tomorrow's - the next Pro level or Pro class would still
    /// need someone to remember. So the parts that <i>can</i> be enumerated are read from the same
    /// assets that do the actual gating: <see cref="LevelCatalog"/> for
    /// <c>LevelDefinition.requiresPro</c> and <see cref="UnitRoster"/> for
    /// <c>UnitClass.requiresPro</c>. Ship a Pro level, and the paywall advertises it with no edit
    /// anywhere. The only hand-written lines left are for perks that have no asset to count.</para>
    ///
    /// <para>Every method degrades to something honest when passed nulls, because a paywall that
    /// renders blank is worse than one that renders a short list.</para>
    /// </summary>
    public static class ProFeatureCopy
    {
        /// <summary>Bullet glyph. Kept to characters the shipped TMP font asset definitely has.</summary>
        private const string Bullet = "<color=#FF8A3D>■</color>  ";

        /// <summary>
        /// Perks that are real, shipped, and not enumerable from an asset.
        ///
        /// <para>The Veteran AI is deliberately listed even though it reaches the player *through*
        /// the Pro level rather than through a gate of its own - it is a distinct thing a buyer
        /// gets, and "harder opponent" is a better reason to subscribe than "one more map".
        /// The saturated palette is <see cref="ScrapSiege.Terrain.TerrainObjectSpawner"/>'s
        /// <c>ProEntitlement.IsUnlocked</c> branch. Nothing else may be added here without a
        /// corresponding gate existing in code - that rule is the entire point of this file.</para>
        /// </summary>
        private static readonly string[] StaticPerks =
        {
            "Veteran-tier AI commander",
            "Saturated terrain palette",
        };

        /// <summary>
        /// The full feature list, one bullet per line, ready to drop into a TMP_Text.
        /// </summary>
        public static string BuildFeatureList(LevelCatalog catalog, UnitRoster roster)
        {
            var lines = new List<string>();

            AppendProLevels(catalog, lines);
            AppendProClasses(roster, lines);
            AppendProSkins(roster, lines);

            foreach (var perk in StaticPerks) lines.Add(perk);

            // A paywall with an empty list would be a worse failure than a slightly generic one:
            // it reads as a broken screen at exactly the moment the player is deciding to pay.
            if (lines.Count == 0) lines.Add("Everything in Scrap Siege, unlocked");

            var builder = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0) builder.Append('\n');
                builder.Append(Bullet).Append(lines[i]);
            }

            return builder.ToString();
        }

        private static void AppendProLevels(LevelCatalog catalog, List<string> lines)
        {
            if (catalog == null) return;

            foreach (var level in catalog.Levels)
            {
                if (level == null || !level.requiresPro) continue;

                lines.Add($"Level {level.levelNumber:00} · {level.displayName}");
            }
        }

        private static void AppendProClasses(UnitRoster roster, List<string> lines)
        {
            if (roster == null) return;

            foreach (var unitClass in roster.Ordered)
            {
                if (unitClass == null || !unitClass.requiresPro) continue;

                lines.Add($"The {unitClass.displayName} unit class");
            }
        }

        /// <summary>
        /// The Veteran skin set, counted rather than asserted.
        ///
        /// <para>Derived like everything else here: the gate is
        /// <c>UnitClassVisual.ResolveModelPrefab</c>, which swaps in <c>UnitClass.proModelPrefab</c>
        /// while the entitlement is active, so counting the classes that have one is counting
        /// exactly what a subscriber will see. Author a sixth skin and this line updates itself;
        /// author none and it disappears rather than promising a set that does not exist.</para>
        ///
        /// <para>Written as one line rather than one per class because it is a *set* - five bullets
        /// naming five skins would push the genuinely different perks off the card.</para>
        /// </summary>
        private static void AppendProSkins(UnitRoster roster, List<string> lines)
        {
            if (roster == null) return;

            int skins = 0;
            foreach (var unitClass in roster.Ordered)
                if (unitClass != null && unitClass.proModelPrefab != null) skins++;

            if (skins == 0) return;

            lines.Add(skins == 1
                ? "A Veteran skin for one unit class"
                : $"Veteran skins for all {skins} unit classes");
        }

        /// <summary>
        /// One-line subtitle under the "SCRAP SIEGE PRO" heading.
        ///
        /// <para>The shipped copy read "Value on top of the full free game - nothing is locked
        /// away", which stopped being true the moment level 05 and the Turret became Pro-gated.
        /// Overstating what the free game contains is the kind of thing that earns a refund
        /// request, and it is trivially checkable by anyone who taps the locked card sitting
        /// directly behind the paywall.</para>
        /// </summary>
        public const string Subtitle = "One subscription. Every level, every unit, the hardest opponent.";
    }
}
