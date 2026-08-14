using UnityEngine;

namespace ScrapSiege.Siege
{
    /// <summary>
    /// What a unit fundamentally *does*. Kept small and explicit rather than expressed purely as
    /// stat spreads, because the three behaviours below need genuinely different code paths in
    /// <see cref="SiegeUnit"/> - a spread of numbers alone cannot make something hold still or
    /// refuse to fight.
    /// </summary>
    public enum UnitRole
    {
        /// <summary>Walks to the enemy base, stops to fight whatever it meets. The original unit.</summary>
        Assault,

        /// <summary>Same, but engages from far outside melee reach and never closes.</summary>
        Marksman,

        /// <summary>
        /// Never advances and never damages a base - a deployable defender. Exists because the AI
        /// commander made defence a real problem for the first time.
        /// </summary>
        Emplacement,
    }

    /// <summary>
    /// One deployable unit type (plan.md Section 6 - unit variety).
    ///
    /// <para><b>Why a ScriptableObject.</b> Same reasoning as <see cref="ScrapSiege.Levels.LevelDefinition"/>:
    /// shipping a new unit should be an asset and a roster entry, not a new prefab, a new script and
    /// a new HUD button. Everything a class changes about a unit - cost, stats, silhouette, whether
    /// it shoots or sneaks - is data read at spawn, so both armies and every level get new classes
    /// for free the moment the asset exists.</para>
    ///
    /// <para><b>Everything spatial is a fraction of board length</b>, per the project-wide rule.
    /// A range in real metres is meaningless when the same level is played on a coffee table and a
    /// dining table.</para>
    ///
    /// <para><b>The frontage rule still governs everything here</b> (see <see cref="SiegeUnit"/>):
    /// one unit fights at most one enemy, whatever its class. Ranged units get *reach*, not the
    /// ability to focus-fire, or the Lanchester failure mode the whole combat design exists to
    /// avoid comes straight back.</para>
    /// </summary>
    [CreateAssetMenu(fileName = "UnitClass", menuName = "Scrap Siege/Unit Class")]
    public class UnitClass : ScriptableObject
    {
        [Header("Identity")]
        public string displayName = "Trooper";

        [Tooltip("Three or four characters for the deploy chip. The chip is roughly a thumb wide on " +
                 "a phone, so anything longer wraps or clips.")]
        public string shortLabel = "TRP";

        [Tooltip("One line explaining what this unit is FOR, shown under the roster bar when selected. " +
                 "Keep it under ~60 characters - it shares a row with the cost and the vantage meter.")]
        [TextArea(1, 3)]
        public string tagline = "Cheap, balanced, reliable.";

        [Tooltip("Sort order in the deploy roster. Lowest first.")]
        public int rosterOrder = 0;

        [Header("Cost")]
        [Tooltip("Scrap per unit. The economy banks a maximum of 10 and ticks 1 every 2s, so a cost " +
                 "of 4 is roughly 'one every eight seconds' and is about as expensive as anything " +
                 "should get without feeling unplayable.")]
        public int cost = 1;

        [Header("Behaviour")]
        public UnitRole role = UnitRole.Assault;

        [Header("Survivability")]
        [Tooltip("Hit points. The baseline is 15, derived from 5 damage per 0.5s tick giving the 1.5s " +
                 "fight plan.md Mechanic 6 specifies. Re-derive rather than guess when changing.\n\n" +
                 "The whole health/damage scale was multiplied by 5 on 2026-08-13 - see the note on " +
                 "attackDamage. Ratios, and therefore fight lengths, are unchanged.")]
        public int health = 15;

        [Tooltip("Damage multiplier while standing in a CoverLane. Lower means this class gets more " +
                 "out of cover than others do.")]
        [Range(0f, 1f)]
        public float coverDamageMultiplier = 0.5f;

        [Tooltip("Sentries do not fire at this class at all. The Saboteur's entire identity - it is " +
                 "what makes a long, watched flank route a real option instead of a punishment.")]
        public bool invisibleToSentries;

        [Header("Movement")]
        [Tooltip("Multiplies the board-relative base speed. 1 crosses the board in the standard " +
                 "16 seconds; 1.6 is a rusher, 0.7 is a slow wall of a unit.")]
        [Range(0f, 3f)]
        public float speedMultiplier = 1f;

        [Header("Combat")]
        [Tooltip("How close an enemy must be before this unit locks onto it, as a fraction of board " +
                 "length. For a Marksman this is its reach and is deliberately far larger than the " +
                 "0.06 melee value - that reach IS the class.")]
        public float engagementRadiusFraction = 0.06f;

        [Tooltip("Seconds between attack ticks. Paired with the opponent's health to set fight length.")]
        public float attackTickSeconds = 0.5f;

        [Tooltip("Damage per attack tick.\n\n" +
                 "The entire health/damage scale is 5x what it was before 2026-08-13. Nothing about " +
                 "the balance changed - every value was multiplied together, so fights last exactly " +
                 "as long. The point is headroom: at the old scale the smallest possible tuning step " +
                 "was 1 damage against 3 HP, i.e. a 33% swing, so 'slightly weaker' was not " +
                 "expressible. At 5x, a 25% adjustment (the Marksman's 5 -> 4) is a real value " +
                 "rather than a rounding decision.\n\n" +
                 "If you change one of these numbers, change it against the 5x scale - do NOT " +
                 "reintroduce a 1 or a 2 unless you genuinely mean 'a fifth of a hit'.")]
        public int attackDamage = 5;

        [Tooltip("After winning a duel this class cannot start another for this long.")]
        public float winnerRecoverySeconds = 0.8f;

        [Tooltip("Never starts a fight, and does not stop when something starts one with it - it " +
                 "walks on and takes the hits. A unit that cannot be pinned but also cannot clear " +
                 "its own path. Emplacements ignore this (they never move regardless).")]
        public bool evadesCombat;

        [Header("Objective")]
        [Tooltip("Damage dealt to the enemy base on arrival. A high value on a fragile, evasive unit " +
                 "is the reward for getting one through untouched. On the post-2026-08-13 5x scale " +
                 "the baseline is 5 against a 40-70 HP base.")]
        public int damageToBase = 5;

        [Header("Look")]
        [Tooltip("The class's own authored model (an FBX from Assets/Models). When set, it REPLACES " +
                 "the shared trooper body entirely, and the primitive silhouette below is skipped.\n\n" +
                 "Must expose children named Torso / Leg_L / Leg_R (and optionally WeaponArm) or " +
                 "UnitAnimator has nothing to drive and the unit will slide about without a gait. " +
                 "Material slots must follow the U_Body / U_Dark / U_Metal / U_Crest naming that " +
                 "MaterialSlots reads, or team colour will not apply.\n\n" +
                 "Height is normalised to the shared trooper's at spawn, so models do NOT need to be " +
                 "authored at a matching size - modelScaleMultiplier stays the only size control.")]
        public GameObject modelPrefab;

        [Tooltip("Optional 'Veteran' re-skin, used instead of the model above while the RevenueCat " +
                 "'pro' entitlement is active. Purely cosmetic: it must NOT differ in reach, size or " +
                 "readability, because the whole point of a cosmetic tier is that it cannot buy an " +
                 "advantage.\n\n" +
                 "Same authoring rules as modelPrefab (Torso / Leg_L / Leg_R children, U_* material " +
                 "slots), and it should be built at the same overall height as the base model - " +
                 "height is normalised at swap time, so a taller veteran just shrinks its own body " +
                 "to compensate and reads as a downgrade.")]
        public GameObject proModelPrefab;

        [Tooltip("Uniform scale multiplier on the unit model. Silhouette is the only " +
                 "thing that distinguishes classes at 5cm once team colour has claimed the palette, " +
                 "so classes must differ in size as well as shape.")]
        [Range(0.5f, 2f)]
        public float modelScaleMultiplier = 1f;

        [Header("Motion")]
        [Tooltip("How this class walks and attacks. Leave 'Override Defaults' off and it moves " +
                 "exactly as every unit did before per-class motion existed.\n\n" +
                 "Worth authoring for two reasons: gross motion is the entire animation budget at " +
                 "5cm (no rig is legible at that size), and the attack style is what stops a " +
                 "Marksman playing the spear THRUST written for the Trooper - a rifle-armed figure " +
                 "lunging forward to stab was reported from device as 'the attack animation seems " +
                 "weird'.")]
        public UnitMotionProfile motion = new UnitMotionProfile();

        [Tooltip("Optional Veteran motion, used instead of the profile above while the RevenueCat " +
                 "'pro' entitlement is active - the movement half of the cosmetic tier. Purely " +
                 "visual: it must not change reach, speed or fight length, because a cosmetic that " +
                 "buys an advantage is not a cosmetic.\n\n" +
                 "Falls back to the base profile when unticked, so a class with a Veteran model but " +
                 "no Veteran gait simply moves normally.")]
        public UnitMotionProfile proMotion = new UnitMotionProfile();

        [Tooltip("Fallback silhouette, used ONLY when modelPrefab is empty: a crude high-contrast " +
                 "accessory built from primitives in code (see UnitClassVisual) so a brand-new class " +
                 "is readable before anyone opens Blender. Every shipped class now has a real model, " +
                 "so this is the prototyping path, not the shipping one.")]
        public ClassSilhouette silhouette = ClassSilhouette.None;

        [Tooltip("Colour of that accessory and of this class's tracer. Never the team colour: the " +
                 "team colour already owns the body, so the accessory has to contrast with it or it " +
                 "disappears (the same reason MaterialSlots leaves the Trim role untinted).")]
        public Color accentColor = new Color(0.98f, 0.62f, 0.16f);

        [Header("Monetization")]
        [Tooltip("Gate this class behind the RevenueCat 'pro' entitlement. Locked classes still show " +
                 "in the roster (with a lock) so the player can see what they are missing - a hidden " +
                 "upsell sells nothing.")]
        public bool requiresPro;

        /// <summary>True if this unit never moves and never attacks a base.</summary>
        public bool IsStationary => role == UnitRole.Emplacement;

        /// <summary>
        /// Emplacements are pure defence - letting one damage a base would make "deploy a turret on
        /// their doorstep" the whole game.
        /// </summary>
        public bool CanAttackBase => role != UnitRole.Emplacement && damageToBase > 0;

        /// <summary>Ranged classes draw a tracer instead of swinging; drives the attack visual only.</summary>
        public bool IsRanged => role == UnitRole.Marksman || role == UnitRole.Emplacement;

        private void OnValidate()
        {
            if (cost < 1) cost = 1;
            if (health < 1) health = 1;
            if (attackDamage < 1) attackDamage = 1;
            if (attackTickSeconds < 0.05f) attackTickSeconds = 0.05f;
            if (engagementRadiusFraction <= 0f) engagementRadiusFraction = 0.06f;

            if (string.IsNullOrWhiteSpace(shortLabel))
                shortLabel = string.IsNullOrEmpty(displayName) ? "UNIT" : displayName.Substring(0, Mathf.Min(3, displayName.Length)).ToUpperInvariant();

            // An emplacement that can still walk is the single most confusing authoring mistake
            // available here, so it is corrected rather than warned about.
            if (role == UnitRole.Emplacement && !Mathf.Approximately(speedMultiplier, 0f))
                speedMultiplier = 0f;
        }
    }

    /// <summary>
    /// The primitive accessory bolted onto the shared trooper model to tell classes apart.
    /// Deliberately crude shapes - at 5cm on a real table only gross silhouette survives.
    /// </summary>
    public enum ClassSilhouette
    {
        None,

        /// <summary>A wide flat plate carried in front - the Vanguard reads as twice as wide.</summary>
        Shield,

        /// <summary>A long thin barrel held out sideways - the Marksman reads as long.</summary>
        Rifle,

        /// <summary>A low swept blade - the Saboteur reads as small and pointed.</summary>
        Blade,

        /// <summary>Replaces the figure entirely with a squat mount and barrel.</summary>
        Turret,
    }
}
