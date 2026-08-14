using UnityEngine;
using UnityEngine.AI;
using ScrapSiege.Core;
using ScrapSiege.Terrain;

namespace ScrapSiege.Siege
{
    /// <summary>
    /// Stationed at a chokepoint by MusterPhaseController. Periodically damages any deployed
    /// SiegeUnit that is inside its covered arc and NOT currently standing in a CoverLane NavMesh
    /// area - this is what gives the route-variety deploy choice (Direct vs. Covered) actual
    /// stakes instead of just being cosmetic path shapes.
    ///
    /// The arc (rather than a full circle) is Mechanic 4: a sentry has a blind side, so physically
    /// walking to another side of the table lets the player deploy into weaker cover. See
    /// SentryArcVisualizer, which draws the wedge on the table so the blind side is readable
    /// without any UI.
    /// </summary>
    public class GarrisonSentry : MonoBehaviour
    {
        [Tooltip("Resolved from the board size at spawn - see ConfigureForBoard. The serialized value " +
                 "is only the fallback for the legacy scan/Fortify flow.")]
        [SerializeField] private float detectionRadius = 0.12f;

        [Tooltip("Detection range as a fraction of board length. At the old absolute 0.2m a sentry " +
                 "covered a THIRD of a 0.60m board, so there was barely anywhere safe to walk and the " +
                 "Direct-vs-Covered route choice lost most of its meaning.\n\n" +
                 "Raised 0.20 -> 0.26 on 2026-08-14, as the other half of 'the sentry does not look " +
                 "like it is shooting at any troops'. Cover lanes becoming board-relative (see " +
                 "TerrainObjectSpawner.coverLaneMarginFraction) opened up roughly 40% of The Narrows " +
                 "as genuinely uncovered ground; 0.20 only reached a third of the way into it, so a " +
                 "unit could walk the open flank and still never be fired on. At 0.26 the open flank " +
                 "is actually watched, which is what the level's briefing has always claimed.")]
        [SerializeField] private float detectionRadiusFraction = 0.26f;

        [SerializeField] private float navMeshSampleFraction = 0.02f;

        [Tooltip("Total width of the covered wedge in degrees, bisected by this object's forward. " +
                 "360 restores the old full-circle behaviour.")]
        [Range(20f, 360f)]
        [SerializeField] private float facingArcDegrees = 150f;

        [SerializeField] private float tickInterval = 0.5f;
        [SerializeField] private int damagePerTick = 5;
        [SerializeField] private float navMeshSampleDistance = 0.1f;

        /// <summary>Read by SentryArcVisualizer so the drawn wedge always matches the real rule.</summary>
        public float DetectionRadius => detectionRadius;

        /// <summary>Read by SentryArcVisualizer so the drawn wedge always matches the real rule.</summary>
        public float FacingArcDegrees => facingArcDegrees;

        /// <summary>
        /// Which side this sentry defends. Only units of the OPPOSING team are ever shot.
        ///
        /// Defaults to Enemy, which is what every sentry has effectively been since garrisons were
        /// added - MusterPhaseController spawns them to defend against the player. Before the AI
        /// commander existed there was only one mobile army, so "damage everything in
        /// SiegeUnit.Active" happened to be correct; the moment a second army joins that same static
        /// list it becomes friendly fire.
        /// </summary>
        public Team Team { get; private set; } = Team.Enemy;

        [Tooltip("Fallback eye height in REAL metres, used only until MusterPhaseController supplies " +
                 "the real vantage. Roughly head height on a sentry figure.")]
        [SerializeField] private float fallbackEyeHeight = 0.03f;

        private float boardLength;
        private SentryFireVisualizer fireVisualizer;

        private Vector3 vantage;
        private bool hasVantage;

        public void SetTeam(Team team) => Team = team;

        /// <summary>
        /// Where this sentry is considered to be WATCHING from - the top of the chokepoint it
        /// garrisons, supplied by <see cref="MusterPhaseController"/>.
        ///
        /// <para><b>Why the eye is not where the model is.</b> A sentry is spawned by sampling the
        /// NavMesh at its anchor's centre, and the anchor carves a hole, so the sentry actually
        /// stands on the ground BESIDE the tower or spire it garrisons. Once line of sight became a
        /// real rule (2026-08-13) that geometry was fatal: a ground-level ray from a sentry standing
        /// next to a wall is blocked by that wall, so every sentry would have been blinded by the
        /// very thing it was posted to watch from - and Blind Spire, whose whole premise is two
        /// sentries stationed behind a central spire, would have stopped functioning entirely.</para>
        ///
        /// <para>Raising the eye to the anchor's top is also just what the fiction already says: a
        /// watchtower sentry watches from the tower. <see cref="SentryFireVisualizer"/> draws its
        /// tracer from the same point, so the rule and the picture cannot disagree - the standing
        /// habit in this file since the arc visualizer was caught drawing at 4% of its real range.
        /// </para>
        /// </summary>
        public void SetVantage(Vector3 worldEyePoint)
        {
            vantage = worldEyePoint;
            hasVantage = true;
        }

        /// <summary>Where this sentry sees and fires from. Public so the visualizer cannot re-derive it.</summary>
        public Vector3 EyePoint => hasVantage
            ? vantage
            : transform.position + Vector3.up * WorldScale.Metres(fallbackEyeHeight);

        /// <summary>
        /// Rescales this sentry's reach to the board it is defending. Called by
        /// MusterPhaseController immediately after spawning, which is after Awake but before Start -
        /// which is exactly why SentryArcVisualizer builds its fan in Start rather than Awake.
        /// </summary>
        public void ConfigureForBoard(float boardLength)
        {
            if (boardLength <= 0f) return;

            this.boardLength = boardLength;
            detectionRadius = detectionRadiusFraction * boardLength;
            navMeshSampleDistance = navMeshSampleFraction * boardLength;

            ApplyBoardScale(boardLength);
        }

        [Tooltip("Board length in REAL metres that the prefab's size is authored against. Must match " +
                 "SiegeUnit.referenceBoardLength, or sentries and the units they shoot at end up " +
                 "different sizes on any board that is not 0.60m.")]
        [SerializeField] private float referenceBoardLength = 0.6f;

        [SerializeField] private Vector2 boardScaleClamp = new Vector2(0.55f, 1.8f);

        private bool boardScaleApplied;

        /// <summary>
        /// Same fix, same reasoning, as <see cref="SiegeUnit.ApplyBoardScale"/> - a sentry that
        /// stayed a fixed real size while the board shrank was the other half of "troops look giant
        /// compared to the map". Kept as a separate copy rather than a shared helper because the two
        /// classes have no common base and the whole rule is four lines; a static utility taking a
        /// Transform and a NavMeshAgent-or-null would be more indirection than the rule is worth.
        /// </summary>
        private void ApplyBoardScale(float boardLength)
        {
            if (boardScaleApplied) return;

            float reference = WorldScale.Metres(referenceBoardLength);
            if (reference <= 0f) return;

            float factor = Mathf.Clamp(boardLength / reference,
                                       Mathf.Min(boardScaleClamp.x, boardScaleClamp.y),
                                       Mathf.Max(boardScaleClamp.x, boardScaleClamp.y));

            boardScaleApplied = true;
            if (Mathf.Approximately(factor, 1f)) return;

            transform.localScale *= factor;
        }

        private void Awake()
        {
            // Same reasoning as SiegeUnit: the prefab is authored at true real-world size, and the
            // AR world is scaled up, so the visual has to follow or the sentry is a fifth the height
            // of the units it is shooting at. detectionRadius/navMeshSampleDistance are REAL-metre
            // fallbacks here - ConfigureForBoard overwrites both with board-relative values the
            // moment MusterPhaseController spawns this, which is right after Awake.
            transform.localScale *= WorldScale.Scale;
            detectionRadius = WorldScale.Metres(detectionRadius);
            navMeshSampleDistance = WorldScale.Metres(navMeshSampleDistance);

            fireVisualizer = GetComponent<SentryFireVisualizer>();
        }

        private void OnEnable()
        {
            InvokeRepeating(nameof(Tick), tickInterval, tickInterval);
        }

        private void OnDisable()
        {
            CancelInvoke(nameof(Tick));
        }

        private void Tick()
        {
            foreach (var unit in SiegeUnit.Active)
            {
                if (unit == null) continue;

                // SiegeUnit.Active holds BOTH armies now. Without this filter an enemy sentry would
                // shoot the AI commander's own advancing units.
                if (unit.Team == Team) continue;
                if (!unit.IsAlive) continue;

                // The Saboteur's whole reason to exist: a class a sentry simply never sees, so the
                // long watched flank becomes a route worth paying for rather than a punishment.
                // Checked before the arc test so a stealth unit costs nothing to skip.
                if (unit.InvisibleToSentries) continue;

                if (!IsInArc(unit.transform.position)) continue;
                if (IsInCoverLane(unit.transform.position)) continue;

                // Terrain now genuinely blocks a sentry, the same way it blocks a Marksman - added
                // 2026-08-13, when "units get shot through walls" was reported. Deliberately checked
                // LAST: it is the only expensive test here, and the two cheap rejections above
                // already remove most candidates.
                //
                // This carves a real sight shadow behind tall pieces. SentryArcVisualizer still
                // draws an un-carved wedge, so the drawn arc now slightly over-promises - left that
                // way on purpose, because Mechanic 3's whole position is that the HUD never tells
                // you WHERE the safe ground is; you find it by moving. Worth revisiting with the
                // sentry overhaul.
                if (!HasFiringLine(unit)) continue;

                // Cover is deliberately still total immunity here rather than the fractional
                // reduction unit-vs-unit combat uses. This is the sentry's long-standing tuned rule
                // and the sentry system is awaiting its own overhaul - changing it now would move
                // balance the user has already device-tested, for no benefit to this pass.
                unit.TakeDamage(damagePerTick);

                // Reported from the damage tick itself, never re-derived, so the tracer physically
                // cannot claim a shot that dealt no damage.
                if (fireVisualizer != null) fireVisualizer.ReportHit(unit, boardLength);
            }
        }

        /// <summary>
        /// Inside the wedge: within range, and within half the arc width of forward. Compared on
        /// the horizontal plane only, so a unit's height above the table never affects whether it
        /// is covered.
        /// </summary>
        private bool IsInArc(Vector3 position)
        {
            Vector3 toUnit = position - transform.position;
            toUnit.y = 0f;

            float distanceSquared = toUnit.sqrMagnitude;
            if (distanceSquared > detectionRadius * detectionRadius) return false;

            // A unit standing exactly on the sentry has no meaningful bearing - treat as covered
            // rather than letting a normalize-by-zero decide it.
            if (distanceSquared < 0.0001f) return true;

            if (facingArcDegrees >= 360f) return true;

            Vector3 forward = transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) return true;

            float angle = Vector3.Angle(forward.normalized, toUnit.normalized);
            return angle <= facingArcDegrees * 0.5f;
        }

        /// <summary>
        /// True if no terrain occluder stands between this sentry's vantage and the unit's own
        /// measured mid-height.
        ///
        /// Uses the same single implementation of the sight rule everything else does
        /// (<see cref="ScrapSiege.Vision.LineOfSightController.HasClearLine"/>) rather than a second
        /// raycast written here - a rule with two implementations is a rule with two behaviours.
        /// </summary>
        private bool HasFiringLine(SiegeUnit unit)
        {
            return ScrapSiege.Vision.LineOfSightController.HasClearLine(
                EyePoint,
                MidHeightOf(unit),
                SiegeLayers.TerrainOccluderMask,
                0f);
        }

        /// <summary>
        /// A unit's visual centre. Aiming at the transform origin would put the ray along the table
        /// surface, where it grazes the board slab and every terrain base plate - so essentially
        /// everything would read as blocked.
        /// </summary>
        private static Vector3 MidHeightOf(Component target)
        {
            bool any = false;
            Bounds bounds = default;

            foreach (var renderer in target.GetComponentsInChildren<Renderer>())
            {
                if (renderer == null || !renderer.enabled) continue;
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }

            return any ? bounds.center : target.transform.position;
        }

        private bool IsInCoverLane(Vector3 position)
        {
            if (!NavMesh.SamplePosition(position, out NavMeshHit hit, navMeshSampleDistance, NavMesh.AllAreas))
                return false;

            return (hit.mask & NavMeshAreas.CoverAreaMask) != 0;
        }
    }
}
