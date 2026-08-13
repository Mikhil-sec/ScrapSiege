using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using ScrapSiege.Core;
using ScrapSiege.Terrain;

namespace ScrapSiege.Siege
{
    /// <summary>
    /// Drives a NavMeshAgent toward a deployed destination and damages the base on arrival.
    /// Also has its own health so GarrisonSentry can chip it down before it gets there, and - since
    /// the AI commander landed - fights enemy units it meets on the way.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class SiegeUnit : MonoBehaviour
    {
        // Fallbacks in REAL metres, overwritten by ConfigureForBoard at spawn. They only matter if a
        // unit is ever created without a board, so they are converted rather than left raw.
        [SerializeField] private float arrivalDistance = 0.15f;
        [SerializeField] private int damageToBase = 5;

        [Tooltip("Derived, not guessed: at 5 damage per 0.5s tick, 15 HP is a 1.5s fight, which is the " +
                 "1-2 seconds of readable combat plan.md Mechanic 6 is specced for. Re-derive this if " +
                 "the tick rate or damage changes. (Health and damage were both multiplied by 5 on " +
                 "2026-08-13 for tuning headroom - see UnitClass.attackDamage.)")]
        [SerializeField] private int health = 15;

        [Header("Movement variety - avoids every unit looking identical")]
        [SerializeField] private float rotationSpeed = 8f;
        [SerializeField] private float speedVarianceMin = 0.85f;
        [SerializeField] private float speedVarianceMax = 1.15f;

        // Offsets the arrival point slightly so a group deployed together doesn't converge on
        // one exact spot single-file. Re-sampled onto the NavMesh so it never lands off-mesh.
        [SerializeField] private float arrivalJitterRadius = 0.1f;

        [Tooltip("How far to either side of the straight line to its target a unit may pick its " +
                 "approach lane, as a fraction of board length. 0 restores the old behaviour where " +
                 "every unit walked the single geometrically-optimal path and an army advanced as " +
                 "one file. This is the main dial for route variety - raise it for a wider fan.")]
        [Range(0f, 0.4f)]
        [SerializeField] private float laneSpreadFraction = 0.16f;

        [Tooltip("Where along the route the lane waypoint sits, as a fraction of the way to the " +
                 "target. Randomised within this range per unit, so units in the same lane still " +
                 "commit to it at different points rather than tracing one shared dog-leg.")]
        [SerializeField] private Vector2 laneWaypointRange = new Vector2(0.35f, 0.65f);

        [Tooltip("Per-unit random spread on how much this agent values cover, as a multiplier on " +
                 "the deploy route's base area cost. Two units sent the same way still disagree " +
                 "about which side of an obstacle is cheaper, which is what stops a stack of units " +
                 "tracing one identical line. 1,1 disables it.")]
        [SerializeField] private Vector2 coverCostVariance = new Vector2(0.7f, 1.5f);

        [Tooltip("Range the agent's avoidance priority is randomised into. Identical priorities make " +
                 "two agents each yield to the other, so they shuffle instead of one cleanly passing.")]
        [SerializeField] private Vector2Int avoidancePriorityRange = new Vector2Int(30, 70);

        [Header("Board-relative tuning (fractions of board length)")]
        [Tooltip("Seconds an average unit takes to cross the whole board, at any board size. " +
                 "Absolute speeds do not work here: the same 0.3 m/s that reads fine on a big table " +
                 "crosses a 0.60m board in two seconds.")]
        [SerializeField] private float boardCrossingSeconds = 16f;

        [Tooltip("How close to the target counts as arrival, as a fraction of board length. Must be " +
                 "small enough that the unit visibly reaches the base - the old absolute 0.15m was a " +
                 "quarter of a 0.60m board, so units struck and vanished well short of it.")]
        [SerializeField] private float arrivalDistanceFraction = 0.045f;

        [SerializeField] private float arrivalJitterFraction = 0.035f;

        [Header("Combat (plan.md Mechanic 6 - frontage-limited)")]
        [Tooltip("How close an enemy must be to start a duel, as a fraction of board length.")]
        [SerializeField] private float engagementRadiusFraction = 0.06f;

        [Tooltip("Seconds between attack ticks. Paired with health to set the fight length.")]
        [SerializeField] private float attackTickSeconds = 0.5f;

        [SerializeField] private int attackDamage = 5;

        [Tooltip("Damage multiplier while standing in a CoverLane. Below 1 means three units in cover " +
                 "beat five in the open - which is what keeps positioning (and therefore Mechanic 4) " +
                 "worth more than raw numbers.")]
        [Range(0f, 1f)]
        [SerializeField] private float coverDamageMultiplier = 0.5f;

        [Tooltip("After winning a duel a unit cannot start another for this long. Stops a survivor " +
                 "chain-killing its way down a queue of arriving enemies, and opens a window where " +
                 "trading a cheap screening unit is genuinely worth it.")]
        [SerializeField] private float winnerRecoverySeconds = 0.8f;

        [SerializeField] private float navMeshSampleFraction = 0.02f;

        [Tooltip("Board length, in REAL metres, that the prefab is authored to look correct on. The " +
                 "trooper model is ~5.2cm, which is right next to a 0.60m battlefield. Every unit is " +
                 "rescaled by (actual board / this) so it keeps that same proportion at any table size.")]
        [SerializeField] private float referenceBoardLength = 0.6f;

        [Tooltip("Clamp on that rescale, so a board pinched to an extreme still fields units that are " +
                 "tappable at the small end and not comically large at the big end.")]
        [SerializeField] private Vector2 boardScaleClamp = new Vector2(0.55f, 1.8f);

        [Header("Class")]
        [Tooltip("Optional. When assigned (normally at spawn via ApplyClass, not in the Inspector) " +
                 "every stat above is overwritten by the class asset. Leaving it null keeps the " +
                 "serialized values, which is what the legacy scan/Fortify flow and any old scene " +
                 "reference still rely on.")]
        [SerializeField] private UnitClass unitClass;

        private static readonly List<SiegeUnit> active = new List<SiegeUnit>();

        /// <summary>All currently-deployed units, BOTH teams - filter by <see cref="Team"/> before acting on them.</summary>
        public static IReadOnlyList<SiegeUnit> Active => active;

        private NavMeshAgent agent;
        private BaseHealth targetBase;
        private UnitAnimator animator;

        // Where this unit is ultimately headed. Kept separate from agent.destination because a
        // Rally order temporarily overrides the destination with a waypoint, and the unit has to
        // know what to resume toward once it gets there.
        private Vector3 finalDestination;

        // One optional waypoint slot serving two purposes: the lane this unit picked for itself at
        // spawn, and a Rally order. A rally deliberately OVERWRITES a lane - the player's order is
        // the more recent and more specific instruction, and honouring a lane after it would make
        // Rally feel like it half-worked.
        private bool hasRouteWaypoint;
        private bool routeWaypointIsRally;

        // Kept explicitly rather than read back off the agent. Combat now overwrites
        // agent.destination while closing on an opponent, so once a fight ends the agent no longer
        // remembers where the waypoint was sending it and the order would be silently dropped.
        private Vector3 routeWaypoint;
        private bool speedVarianceApplied;
        private bool hasTarget;

        // Health is tracked as a float so the cover multiplier can be a genuine fraction. Authoring
        // stays an int (an Inspector "3 hit points" is readable; "3.0" invites false precision), but
        // halving integer damage would floor to zero and make cover total immunity by accident.
        private float currentHealth;
        private bool dying;

        private float engagementRadius;
        private float navMeshSampleDistance;

        // laneSpreadFraction resolved against the real board at ConfigureForBoard. Zero until then,
        // which correctly disables lane picking on the legacy path that never supplies a board.
        private float laneSpread;
        private float attackTimer;
        private float recoveryRemaining;
        private SiegeUnit duelOpponent;

        // The reach the CURRENT duel was opened at, copied to both sides when it begins. Without a
        // shared value the two halves of one fight disagree about when it has ended: a marksman
        // engaging at 0.16 of the board would still consider the duel live while its melee opponent,
        // checking against its own 0.06, had already released and walked off.
        private float duelExitRadius;

        private bool classApplied;
        private bool boardScaleApplied;

        // Only used to scale the tracer width, so it is cached rather than plumbed through the
        // combat calls. Zero on the legacy path, which CombatFx handles with a real-metre fallback.
        private float boardLengthForFx;

        public NavMeshAgent Agent => agent;

        /// <summary>The class this unit was spawned as. Null on the legacy scan/Fortify path.</summary>
        public UnitClass Class => unitClass;

        /// <summary>
        /// Sentries skip this unit entirely. Read by <see cref="GarrisonSentry"/> - the sneak route
        /// only exists because something can walk it unpunished.
        /// </summary>
        public bool InvisibleToSentries => unitClass != null && unitClass.invisibleToSentries;

        /// <summary>True for an Emplacement: it holds its ground and never attacks a base.</summary>
        public bool IsStationary => unitClass != null && unitClass.IsStationary;

        /// <summary>Which side this unit fights for. Set by whoever spawns it, before SetTarget.</summary>
        public Team Team { get; private set; } = Team.Player;

        /// <summary>True while diverting to a rally waypoint rather than heading for the base.</summary>
        public bool IsRallying => hasRouteWaypoint && routeWaypointIsRally;

        /// <summary>
        /// True while locked in a duel. This is the frontage cap: a unit already fighting cannot be
        /// picked as anyone else's target, so damage never concentrates.
        /// </summary>
        public bool IsEngaged => duelOpponent != null;

        /// <summary>False once the unit has started dying, so nothing targets or damages a corpse.</summary>
        public bool IsAlive => !dying;

        /// <summary>
        /// This unit's own multiplier on the cover area cost, rolled once at Awake. Pass it to
        /// <see cref="ScrapSiege.Terrain.NavMeshAreas.ApplyCoverPreference"/> so every spawn site
        /// gets route variety from the one roll rather than each rolling its own (and one of them
        /// eventually forgetting to).
        /// </summary>
        public float CoverCostVariance { get; private set; } = 1f;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            animator = GetComponent<UnitAnimator>();

            CoverCostVariance = Random.Range(Mathf.Min(coverCostVariance.x, coverCostVariance.y),
                                             Mathf.Max(coverCostVariance.x, coverCostVariance.y));

            arrivalDistance = WorldScale.Metres(arrivalDistance);
            arrivalJitterRadius = WorldScale.Metres(arrivalJitterRadius);
            engagementRadius = WorldScale.Metres(0.04f);
            navMeshSampleDistance = WorldScale.Metres(0.02f);

            currentHealth = health;

            // The prefab is authored at true real-world size - a 5.2cm trooper with a ~1.4cm agent
            // radius. The AR world is scaled up by WorldScale.Scale, so both the visual and the
            // agent's own avoidance dimensions have to follow, or a correctly-sized board is walked
            // by units a fifth of the height they should be. Awake runs exactly once per instance,
            // so this cannot compound. Speed and stoppingDistance are deliberately NOT touched here:
            // ConfigureForBoard derives them from board length, which is already scaled.
            transform.localScale *= WorldScale.Scale;
            if (agent != null)
            {
                agent.radius *= WorldScale.Scale;
                agent.height *= WorldScale.Scale;
                agent.baseOffset *= WorldScale.Scale;

                // Two agents on equal avoidance priority each treat the other as an obstacle to
                // yield to, so a pair meeting in a corridor shuffles sideways in lockstep instead of
                // one simply going through. Spreading the priority makes crowds resolve into a flow
                // rather than a scrum, and it costs one assignment per unit.
                agent.avoidancePriority = Random.Range(
                    Mathf.Min(avoidancePriorityRange.x, avoidancePriorityRange.y),
                    Mathf.Max(avoidancePriorityRange.x, avoidancePriorityRange.y) + 1);
            }
        }

        private void OnEnable() => active.Add(this);

        private void OnDisable()
        {
            active.Remove(this);

            // Never leave an opponent locked to a unit that no longer exists - it would be unable to
            // ever engage anything again and would stand there permanently stopped.
            ReleaseDuel();
        }

        /// <summary>
        /// Assigns this unit's side. Call before <see cref="SetTarget"/>.
        ///
        /// Team.Player is the default so the pre-existing deploy path keeps working untouched if it
        /// ever forgets to call this - a unit that silently defects to the enemy would be a
        /// spectacularly confusing bug.
        /// </summary>
        public void SetTeam(Team team) => Team = team;

        /// <summary>
        /// Stamps a <see cref="UnitClass"/> onto this instance. Call at spawn, BEFORE
        /// <see cref="ConfigureForBoard"/> (which reads the engagement fraction and applies the speed
        /// multiplier) and before <see cref="SetTarget"/>.
        ///
        /// <para>Guarded against a second application: the model scale and agent radius are
        /// *multiplied* here, so calling it twice would produce a unit twice the size it should be -
        /// the same compounding trap <see cref="SetTarget"/>'s speed variance already guards.</para>
        ///
        /// <para>A null argument is a no-op rather than a reset, so a caller that has no class to
        /// give (the legacy scan/Fortify path) leaves the prefab's own serialized stats intact.</para>
        /// </summary>
        public void ApplyClass(UnitClass definition)
        {
            if (definition == null || classApplied) return;
            classApplied = true;

            unitClass = definition;

            health = definition.health;
            currentHealth = health;
            coverDamageMultiplier = definition.coverDamageMultiplier;
            engagementRadiusFraction = definition.engagementRadiusFraction;
            attackTickSeconds = definition.attackTickSeconds;
            attackDamage = definition.attackDamage;
            winnerRecoverySeconds = definition.winnerRecoverySeconds;
            damageToBase = definition.damageToBase;

            if (!Mathf.Approximately(definition.modelScaleMultiplier, 1f))
            {
                transform.localScale *= definition.modelScaleMultiplier;
                if (agent != null) agent.radius *= definition.modelScaleMultiplier;
            }

            var visual = GetComponent<UnitClassVisual>();
            if (visual != null) visual.Apply(definition);

            // An emplacement is parked for its whole life. The agent is deliberately kept ENABLED
            // rather than disabled or removed, so it still occupies avoidance space and advancing
            // units path around the turret instead of walking through it.
            if (definition.IsStationary && agent != null && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.velocity = Vector3.zero;
            }
        }

        /// <summary>
        /// Rescales this unit's movement to the board it is fighting on. Call before
        /// <see cref="SetTarget"/> so the speed variance multiplies the correct base speed.
        ///
        /// Everything here was authored as absolute metres, which quietly stopped making sense once
        /// levels became normalised: a unit crossed a 0.60m board in 2 seconds, and "arrived" - dealt
        /// its damage and despawned - a full 0.15m short of the base, so the win fired without the
        /// player ever seeing a unit reach it.
        /// </summary>
        public void ConfigureForBoard(float boardLength)
        {
            if (boardLength <= 0f) return;
            if (agent == null) agent = GetComponent<NavMeshAgent>();

            boardLengthForFx = boardLength;
            ApplyBoardScale(boardLength);
            arrivalDistance = arrivalDistanceFraction * boardLength;
            arrivalJitterRadius = arrivalJitterFraction * boardLength;
            laneSpread = laneSpreadFraction * boardLength;
            engagementRadius = engagementRadiusFraction * boardLength;
            navMeshSampleDistance = navMeshSampleFraction * boardLength;

            if (agent == null) return;

            agent.speed = boardLength / Mathf.Max(boardCrossingSeconds, 0.1f);
            if (unitClass != null) agent.speed *= unitClass.speedMultiplier;
            agent.acceleration = agent.speed * 4f;
            // Must stay under arrivalDistance, or the agent parks itself before Update ever counts
            // it as arrived and the unit sits next to the base doing nothing.
            agent.stoppingDistance = arrivalDistance * 0.4f;
        }

        /// <summary>
        /// Sizes the unit against the board it is actually fighting on.
        ///
        /// <para><b>The last absolute size in the project.</b> Everything else - speed, reach,
        /// arrival radius, sentry range, terrain height - became a fraction of board length in the
        /// 2026-08-08 pass, but a unit's own model kept a fixed real size, because
        /// <see cref="Awake"/> multiplies the prefab by <see cref="WorldScale.Scale"/> and stops
        /// there. That is correct for the AR world scale and wrong for the board: the trooper is
        /// ~5.2cm whether the player fitted the battlefield to a dining table or a side table, so on
        /// a small board the troops stood taller than the cover they were supposed to hide behind
        /// and the whole miniature read broke. Reported from device on 2026-08-13 as "troops look
        /// giant compared to the map".</para>
        ///
        /// <para>Applied here rather than in <see cref="Awake"/> because the board is not known
        /// until spawn, and multiplied (not assigned) so it composes with both the world scale and
        /// the class's own <c>modelScaleMultiplier</c> instead of overwriting either. Guarded like
        /// <see cref="ApplyClass"/> is, for the same reason: <see cref="ConfigureForBoard"/> is not
        /// contractually once-per-unit, and a second application would square the factor.</para>
        ///
        /// <para>The agent's radius/height follow the model. If they did not, a unit shrunk to 60%
        /// would still claim its full avoidance footprint and shoulder its neighbours around a
        /// corridor it visually fits through.</para>
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
            if (agent != null)
            {
                agent.radius *= factor;
                agent.height *= factor;
                agent.baseOffset *= factor;
            }
        }

        public void SetTarget(Vector3 position, BaseHealth targetBaseHealth)
        {
            // An emplacement is a defensive placement: it holds the ground it was dropped on and has
            // no objective of its own. Accepting the call and ignoring it (rather than making every
            // spawn site branch) keeps the deploy paths identical for all classes.
            if (IsStationary)
            {
                targetBase = null;
                hasTarget = false;
                FaceTowards(position);
                return;
            }

            targetBase = targetBaseHealth;
            hasTarget = true;

            // Guarded so a second SetTarget call can't compound the multiplier into a unit that
            // sprints across the whole table.
            if (!speedVarianceApplied)
            {
                speedVarianceApplied = true;
                agent.speed *= Random.Range(speedVarianceMin, speedVarianceMax);
            }

            finalDestination = JitteredDestination(position);
            hasRouteWaypoint = false;
            routeWaypointIsRally = false;

            // Picked before the first SetDestination, so a unit never briefly commits to the direct
            // line and then visibly swerves onto its lane a frame later.
            PickApproachLane(finalDestination);

            agent.SetDestination(hasRouteWaypoint ? routeWaypoint : finalDestination);
        }

        /// <summary>
        /// Gives this unit its own way across the board.
        ///
        /// <para><b>The problem this solves.</b> A NavMeshAgent asked for a destination returns the
        /// single geometrically optimal corner path, and every agent with the same start, the same
        /// destination and the same area costs gets the *same* one - so an army advanced as a
        /// single file tracing one polyline, which is exactly the "all units follow a strict path"
        /// report from the 2026-08-13 device test. Speed variance and arrival jitter, which were
        /// the only variety in the system, change when a unit arrives and where it stops; neither
        /// changes the route, so they could never fix this.</para>
        ///
        /// <para><b>Why a waypoint rather than steering noise.</b> Nudging the destination each
        /// frame produces wandering, not routes: a unit that wobbles reads as badly-driven rather
        /// than as having chosen a flank, and it fights the avoidance system the whole way. One
        /// committed waypoint, offset perpendicular to the advance, produces a genuine second lane
        /// that respects terrain, cover costs and chokepoints because the NavMesh still solves both
        /// halves properly. It also costs nothing per frame.</para>
        ///
        /// <para><b>Why the second path is verified.</b> A waypoint that is reachable but from which
        /// the base is NOT (across a wall, in a pocket) would leave the unit standing at the
        /// waypoint for the rest of the match with a silently invalid path - this project has
        /// already lost a session to <see cref="NavMeshAgent.remainingDistance"/> reading 0 for
        /// exactly that state. So the onward leg is proven complete before the lane is accepted, and
        /// the unit falls back to the direct route if it is not. Two path solves at spawn only.</para>
        /// </summary>
        private void PickApproachLane(Vector3 destination)
        {
            if (laneSpread <= 0f || agent == null || !agent.isOnNavMesh) return;

            Vector3 start = transform.position;
            Vector3 toTarget = destination - start;
            toTarget.y = 0f;

            // Too close to be worth a detour - the dog-leg would read as the unit changing its mind.
            if (toTarget.magnitude < laneSpread * 2f) return;

            Vector3 forward = toTarget.normalized;
            Vector3 lateral = Vector3.Cross(Vector3.up, forward);

            float alongFraction = Random.Range(Mathf.Min(laneWaypointRange.x, laneWaypointRange.y),
                                               Mathf.Max(laneWaypointRange.x, laneWaypointRange.y));
            Vector3 candidate = start + toTarget * alongFraction
                                + lateral * Random.Range(-1f, 1f) * laneSpread;

            // Generous search: a lane aimed into a wall should resolve to the walkable ground beside
            // it rather than be thrown away, since "hug this side of the obstacle" is exactly the
            // route worth having.
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, laneSpread * 1.5f, agent.areaMask))
                return;

            var onward = new NavMeshPath();
            if (!NavMesh.CalculatePath(hit.position, destination, agent.areaMask, onward)
                || onward.status != NavMeshPathStatus.PathComplete)
                return;

            hasRouteWaypoint = true;
            routeWaypointIsRally = false;
            routeWaypoint = hit.position;
        }

        /// <summary>
        /// Diverts this unit through a waypoint before continuing to its base target. Issued by
        /// RallyController when the player is pulled back far enough to command the whole board.
        /// Returns false if the waypoint isn't reachable, so the caller can avoid charging for a
        /// no-op order.
        /// </summary>
        public bool RallyTo(Vector3 waypoint, float snapDistance)
        {
            if (agent == null || !agent.isOnNavMesh) return false;

            if (!NavMesh.SamplePosition(waypoint, out NavMeshHit hit, snapDistance, agent.areaMask))
                return false;

            // Overwrites whatever lane this unit picked for itself. A player order outranks the
            // unit's own routing preference; keeping the lane would make Rally look half-obeyed.
            hasRouteWaypoint = true;
            routeWaypointIsRally = true;
            routeWaypoint = hit.position;
            agent.SetDestination(routeWaypoint);
            return true;
        }

        /// <summary>
        /// Puts the agent back on whatever it was doing before a fight interrupted it. Needed
        /// because <see cref="UpdateEngagedMovement"/> repoints the destination at the opponent.
        /// </summary>
        private void ResumeRoute()
        {
            if (agent == null || !agent.isOnNavMesh) return;
            if (IsStationary || !hasTarget) return;

            agent.SetDestination(hasRouteWaypoint ? routeWaypoint : finalDestination);
        }

        private Vector3 JitteredDestination(Vector3 position)
        {
            if (arrivalJitterRadius <= 0f) return position;

            Vector2 offset = Random.insideUnitCircle * arrivalJitterRadius;
            Vector3 jittered = position + new Vector3(offset.x, 0f, offset.y);

            // Search radius scales with the jitter itself - a fixed +0.1m margin was a sixth of a
            // 0.60m board and could re-snap the destination somewhere quite unintended.
            if (NavMesh.SamplePosition(jittered, out NavMeshHit hit, arrivalJitterRadius * 2f, NavMesh.AllAreas))
                return hit.position;

            return position;
        }

        /// <summary>
        /// Applies damage. <paramref name="applyCover"/> is false for GarrisonSentry, which handles
        /// cover by skipping covered units outright (its own long-standing rule, left alone while the
        /// sentry system awaits its overhaul), and true for unit-vs-unit damage, where cover is a
        /// reduction rather than immunity.
        /// </summary>
        public void TakeDamage(int amount, bool applyCover = false)
        {
            if (dying) return;

            float scaled = amount;
            if (applyCover && IsInCoverLane()) scaled *= coverDamageMultiplier;

            currentHealth -= scaled;
            if (currentHealth <= 0f) Die();
        }

        private void Die()
        {
            if (dying) return;
            dying = true;

            // Free the opponent before the effect plays, so the survivor starts recovering (and can
            // resume walking) on the same frame rather than standing over a corpse.
            //
            // The survivor needs its OWN ReleaseDuel call, not just the back-reference clear: agents
            // are halted via agent.isStopped when a duel begins, and clearing the reference alone
            // leaves the winner permanently frozen - it would never re-enter the engaged branch that
            // resumes it, so it would stand still for the rest of the match.
            SiegeUnit opponent = duelOpponent;
            ReleaseDuel();
            if (opponent != null)
            {
                opponent.ReleaseDuel();
                opponent.BeginRecovery();
            }

            UnitDeathEffect.Play(gameObject, transform.position.y);
            Destroy(gameObject);
        }

        private void BeginRecovery() => recoveryRemaining = winnerRecoverySeconds;

        private bool IsInCoverLane()
        {
            if (!NavMesh.SamplePosition(transform.position, out NavMeshHit hit, navMeshSampleDistance, NavMesh.AllAreas))
                return false;

            return (hit.mask & NavMeshAreas.CoverAreaMask) != 0;
        }

        private void Update()
        {
            if (dying) return;

            if (recoveryRemaining > 0f)
                recoveryRemaining = Mathf.Max(0f, recoveryRemaining - Time.deltaTime);

            UpdateCombat();

            // A unit locked in a duel is either standing and fighting or closing on its opponent -
            // either way UpdateEngagedMovement owns its navigation, so skip the base-advance and
            // arrival logic rather than letting a stopped agent's zeroed remainingDistance be
            // misread as having arrived.
            //
            // The exception is an evading class (the Saboteur): being shot at does not stop it, so
            // it keeps running its original route while the duel is nominally live.
            if (IsEngaged && !Evades) return;

            FaceMovementDirection();
            UpdateNavigation();
        }

        private bool Evades => unitClass != null && unitClass.evadesCombat;

        /// <summary>How far this unit can actually deal damage from - its own class's reach.</summary>
        private float AttackRange => engagementRadius;

        /// <summary>
        /// The frontage rule (plan.md Mechanic 6).
        ///
        /// A unit fights at most one enemy, and looks only for enemies that are not already fighting
        /// someone. If every enemy in reach is busy, it finds nothing and simply walks on - which is
        /// the whole point: a numerical advantage turns into units flowing PAST the fight toward the
        /// objective, not into several units beating on one. Dogpiling would make losses scale by
        /// Lanchester's square law, so the larger force would always win and "deploy the maximum
        /// number of units" would be strictly correct, which flattens positioning, vantage and cover
        /// into irrelevance.
        /// </summary>
        private void UpdateCombat()
        {
            if (IsEngaged)
            {
                if (!IsDuelStillValid())
                {
                    ReleaseDuel();
                    return;
                }

                UpdateEngagedMovement();
                return;
            }

            // An evading class never starts a fight - it has somewhere to be. It can still be
            // engaged BY something, which is how it takes damage on the way past.
            if (Evades) return;

            // Recovery blocks STARTING a fight, never defending one. A unit that has just won is
            // briefly vulnerable rather than briefly invincible.
            if (recoveryRemaining > 0f) return;

            SiegeUnit target = FindUnengagedEnemy();
            if (target != null) BeginDuel(target);
        }

        /// <summary>
        /// The asymmetric half of combat, and the thing that makes a Marksman a Marksman.
        ///
        /// <para>A duel locks two units together, but they do not necessarily fight from the same
        /// distance. Each side independently checks the range IT can shoot from: a marksman that
        /// opened at 0.16 of the board is already in range and stands still firing, while the
        /// assault unit it hit is far outside its own 0.06 melee reach, so it closes the gap
        /// instead. That exchange - free damage while the enemy walks in, then a melee fight the
        /// marksman loses - is the whole trade the class is built on, and it falls out of one
        /// range comparison rather than a separate ranged-combat system.</para>
        /// </summary>
        private void UpdateEngagedMovement()
        {
            // Being shot does not stop an evader. It keeps its original destination and simply
            // absorbs the hits, so the counter-play is killing it, never pinning it.
            if (Evades) return;

            float sqrDistance = (duelOpponent.transform.position - transform.position).sqrMagnitude;
            bool inRange = sqrDistance <= AttackRange * AttackRange;

            if (inRange)
            {
                StopForCombat();
                FightTick();
                return;
            }

            // Out of our own reach: close on the opponent. Emplacements cannot, so they simply wait
            // for it to come to them (and will hold the duel open until the exit radius breaks it).
            if (IsStationary)
            {
                FaceOpponent();
                return;
            }

            if (agent != null && agent.isOnNavMesh)
            {
                agent.isStopped = false;
                agent.SetDestination(duelOpponent.transform.position);
            }

            FaceMovementDirection();
        }

        private bool IsDuelStillValid()
        {
            if (duelOpponent == null || !duelOpponent.IsAlive) return false;

            // Generous exit range so two units that drift slightly apart mid-fight (agent avoidance
            // nudges them) don't flicker in and out of the duel. duelExitRadius is the range the
            // duel was OPENED at, shared by both sides - see the field comment for why deriving it
            // per-side breaks long-range engagements.
            float exitRadius = duelExitRadius * 1.6f;
            return (duelOpponent.transform.position - transform.position).sqrMagnitude
                   <= exitRadius * exitRadius;
        }

        private SiegeUnit FindUnengagedEnemy()
        {
            SiegeUnit nearest = null;
            float nearestSqr = engagementRadius * engagementRadius;

            foreach (var other in active)
            {
                if (other == null || other == this) continue;
                if (!other.IsAlive) continue;
                if (other.Team == Team) continue;

                // The frontage cap itself: an enemy already in a duel is not a candidate.
                if (other.IsEngaged) continue;

                float sqr = (other.transform.position - transform.position).sqrMagnitude;
                if (sqr > nearestSqr) continue;

                nearestSqr = sqr;
                nearest = other;
            }

            return nearest;
        }

        /// <summary>Locks both units into the duel. Symmetric, so neither can be pulled into a second one.</summary>
        private void BeginDuel(SiegeUnit other)
        {
            duelOpponent = other;
            other.duelOpponent = this;

            // Both sides judge the duel's end against the range it was opened at - the initiator's.
            duelExitRadius = engagementRadius;
            other.duelExitRadius = engagementRadius;

            // Only stop what is actually in range. The old unconditional double-stop froze a melee
            // unit in place the instant a marksman across the board locked onto it, so it stood
            // still being shot and never closed - the exact opposite of the intended trade.
            StopForCombat();
            if (!other.Evades && !other.IsStationary)
            {
                float sqr = (other.transform.position - transform.position).sqrMagnitude;
                if (sqr <= other.AttackRange * other.AttackRange) other.StopForCombat();
            }

            // Staggered so two units that meet head-on do not land every blow on the same frame and
            // annihilate each other simultaneously - a fight should usually leave a survivor.
            attackTimer = Random.Range(0f, attackTickSeconds * 0.5f);
            other.attackTimer = Random.Range(0f, other.attackTickSeconds * 0.5f);
        }

        private void StopForCombat()
        {
            if (agent != null && agent.isOnNavMesh) agent.isStopped = true;
        }

        private void ReleaseDuel()
        {
            if (duelOpponent != null)
            {
                // Clear the far side's back-reference only if it still points at us, so releasing a
                // stale duel cannot detach an opponent from a fight it has since joined.
                if (duelOpponent.duelOpponent == this) duelOpponent.duelOpponent = null;
                duelOpponent = null;
            }

            // An emplacement must stay stopped for its whole life - releasing a duel is not
            // permission for a turret to start walking.
            if (agent != null && agent.isOnNavMesh && !IsStationary) agent.isStopped = false;

            ResumeRoute();
        }

        private void FightTick()
        {
            FaceOpponent();

            // A unit still recovering from its last kill does not swing yet, which hands the
            // initiative to whoever caught it.
            if (recoveryRemaining > 0f) return;

            attackTimer -= Time.deltaTime;
            if (attackTimer > 0f) return;

            attackTimer = attackTickSeconds;

            if (animator != null) animator.PlayAttack();

            // A ranged class draws its shot, because at this distance there is nothing else on
            // screen connecting shooter and victim - exactly the problem SentryFireVisualizer was
            // built to solve for sentries.
            if (unitClass != null && unitClass.IsRanged) DrawShotAtOpponent();

            // Every blow lands somewhere visible, ranged or not. Melee previously produced nothing
            // but a lunge, so a fight between two units read as loitering rather than as combat -
            // the single biggest thing making the board look inert on a phone.
            DrawImpactOnOpponent();

            duelOpponent.TakeDamage(attackDamage, applyCover: true);
        }

        private void DrawShotAtOpponent()
        {
            if (duelOpponent == null) return;

            // Fire from and to roughly chest height rather than the transform origins, which sit on
            // the table - a tracer between two origins draws along the floor and reads as a decal.
            float lift = engagementRadius * 0.12f;
            Vector3 muzzle = transform.position + Vector3.up * lift;
            Vector3 impact = duelOpponent.transform.position + Vector3.up * lift;

            CombatFx.Shot(muzzle, impact, unitClass.accentColor, boardLengthForFx);

            ScrapSiege.Audio.GameAudio.Play(
                unitClass.role == UnitRole.Emplacement
                    ? ScrapSiege.Audio.Sfx.TurretFire
                    : ScrapSiege.Audio.Sfx.MarksmanShot,
                0.55f);
        }

        /// <summary>
        /// The spark where a blow lands. Aimed at the opponent's own measured mid-height rather than
        /// its transform origin, which sits on the table - a burst at the origin looks like the floor
        /// cracking, not like a unit being hit. Same reasoning as
        /// <see cref="SentryFireVisualizer.AimPoint"/>.
        /// </summary>
        private void DrawImpactOnOpponent()
        {
            if (duelOpponent == null) return;

            Color color = unitClass != null ? unitClass.accentColor : new Color(1f, 0.72f, 0.32f);
            CombatFx.Impact(MidHeightOf(duelOpponent), color, boardLengthForFx);
        }

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

        private void FaceOpponent()
        {
            if (duelOpponent == null) return;

            Vector3 toOpponent = duelOpponent.transform.position - transform.position;
            toOpponent.y = 0f;
            if (toOpponent.sqrMagnitude < 1e-6f) return;

            Quaternion look = Quaternion.LookRotation(toOpponent.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, Time.deltaTime * rotationSpeed);
        }

        private void UpdateNavigation()
        {
            // Nothing to advance on yet. Without this an AI unit spawned before its target resolves
            // would run the arrival check against a zeroed destination. An emplacement never has a
            // target at all, so the same guard parks it permanently.
            if (!hasTarget || IsStationary) return;

            if (agent.pathPending) return;

            // NavMeshAgent.remainingDistance silently defaults to 0 - not Infinity, not an error -
            // whenever the agent has no valid path (hasPath false, e.g. off-mesh or pathStatus
            // PathInvalid). That is indistinguishable from "arrived" unless checked for explicitly,
            // and was confirmed (via an in-Editor Play mode trace) to be exactly how a unit could
            // reach 0 remainingDistance and deal base damage within a couple of frames of spawning,
            // nowhere near the actual base. If a unit ever genuinely loses its path mid-match, the
            // correct failure mode is "stands still", not "silently counts as arrived".
            if (!agent.hasPath || agent.pathStatus != NavMeshPathStatus.PathComplete) return;

            if (agent.remainingDistance > arrivalDistance) return;

            // Reached a waypoint (its own lane, or a rally point), not the base - resume the advance.
            if (hasRouteWaypoint)
            {
                hasRouteWaypoint = false;
                routeWaypointIsRally = false;
                agent.SetDestination(finalDestination);
                return;
            }

            if (targetBase != null)
            {
                targetBase.TakeDamage(damageToBase);

                // The whole objective of the game, and until now it happened in silence: the unit
                // simply blinked out and a number changed on a bar. A bigger burst at the point of
                // contact is the one moment in a match that should look like it mattered.
                // Visual only - BaseHealth.TakeDamage already plays Sfx.BaseHit, and sounding it
                // here too would be the same double-audio bug just fixed on the UI buttons.
                Color color = unitClass != null ? unitClass.accentColor : new Color(1f, 0.62f, 0.22f);
                CombatFx.Impact(MidHeightOf(targetBase), color, boardLengthForFx, scale: 2.6f);
            }
            else
                Debug.LogWarning($"{name}: arrived but has no BaseHealth target - no damage dealt. Check SiegePhaseController.DummyBaseHealth wiring.", this);

            Destroy(gameObject);
        }

        /// <summary>
        /// Snaps to look at a point. Used for an emplacement, which never moves and so would
        /// otherwise face whatever arbitrary direction it was instantiated with - a turret aimed at
        /// its owner's own base reads as broken even though it fires in every direction anyway.
        /// </summary>
        private void FaceTowards(Vector3 point)
        {
            Vector3 toPoint = point - transform.position;
            toPoint.y = 0f;
            if (toPoint.sqrMagnitude < 1e-6f) return;

            transform.rotation = Quaternion.LookRotation(toPoint.normalized, Vector3.up);
        }

        private void FaceMovementDirection()
        {
            Vector3 flatVelocity = agent.velocity;
            flatVelocity.y = 0f;
            if (flatVelocity.sqrMagnitude < 0.001f) return;

            Quaternion targetRotation = Quaternion.LookRotation(flatVelocity.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * rotationSpeed);
        }
    }
}
