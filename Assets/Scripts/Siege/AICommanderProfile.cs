using UnityEngine;

namespace ScrapSiege.Siege
{
    /// <summary>
    /// One difficulty tier for the AI commander, as data rather than code.
    ///
    /// <para>Every number the commander reasons with lives here on purpose. "Rule-based, explicit
    /// thresholds" is only true if the thresholds are visible and tunable - the moment they are
    /// scattered through the behaviour as literals, difficulty becomes a code change and balancing
    /// stops being something the designer can do. It also keeps the project's zero-ML constraint
    /// self-evidently satisfied: there is nothing here but authored numbers.</para>
    ///
    /// <para>Difficulty comes from <b>decision quality and reaction speed, never from cheating</b> -
    /// the AI ticks the same <see cref="ResourceEconomy"/> component the player does, just at a rate
    /// this asset scales.</para>
    /// </summary>
    [CreateAssetMenu(fileName = "AICommanderProfile", menuName = "Scrap Siege/AI Commander Profile")]
    public class AICommanderProfile : ScriptableObject
    {
        [Header("Identity")]
        public string displayName = "Recruit";

        [Header("Cadence")]
        [Tooltip("Seconds between decisions. ~1s reads as deliberate; much faster feels twitchy and " +
                 "much slower feels asleep.")]
        public float decisionTickSeconds = 1f;

        [Tooltip("A threat must persist this long before the commander reacts to it. This is the main " +
                 "difficulty dial and the main READABILITY dial - an AI that responds instantly feels " +
                 "clairvoyant, and the player never gets to see their feint work.")]
        public float reactionDelaySeconds = 1.5f;

        [Header("Economy (symmetric - the AI does not cheat)")]
        [Tooltip("Multiplies the player's resource tick interval. Above 1 is SLOWER than the player, " +
                 "which is how the first tier stays winnable.")]
        public float resourceIntervalMultiplier = 1.35f;

        [Header("Costs and commitment")]
        public int pushCost = 1;
        public int interceptCost = 1;

        [Tooltip("Resources the commander tries to bank before pushing. This is 'hold for a bigger " +
                 "wave' - without it the AI dribbles out single units that die one at a time and never " +
                 "reads as an attack.")]
        public int holdBankTarget = 3;

        [Tooltip("Hard cap on the AI's live units, so a long stalemate cannot end in a flood the " +
                 "player has no answer to.")]
        public int maxLiveUnits = 6;

        [Header("Readability")]
        [Tooltip("Seconds between the commander committing to a wave and its units appearing. " +
                 "Telegraphing beats optimal play here - this is a demo-video game as much as a " +
                 "strategy game, and an attack the player cannot see coming just feels arbitrary.")]
        public float telegraphLeadSeconds = 1.2f;

        [Header("Behaviour")]
        [Tooltip("Chance a deployed unit prefers cover, standing in for a real unit mix until more " +
                 "unit types exist.")]
        [Range(0f, 1f)]
        public float coveredPreferenceChance = 0.4f;

        [Tooltip("How advanced a player unit must be, as a fraction of the board, before it counts as " +
                 "a threat worth intercepting. 0 = the moment it is deployed, 1 = only at the base.")]
        [Range(0f, 1f)]
        public float interceptThreatThreshold = 0.35f;

        [Tooltip("Candidate lanes sampled across the board when choosing where to push. More lanes " +
                 "means finer lane selection and a slightly slower tick.")]
        [Range(3, 9)]
        public int laneSamples = 5;

        [Tooltip("How far in front of its own base the AI deploys, as a fraction of board length. " +
                 "Keeps its units from spawning on top of the objective they are defending.")]
        public float spawnOffsetFraction = 0.1f;
    }
}
