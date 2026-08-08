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
        [SerializeField] private int damageToBase = 1;

        [Tooltip("Derived, not guessed: at 1 damage per 0.5s tick, 3 HP is a 1.5s fight, which is the " +
                 "1-2 seconds of readable combat plan.md Mechanic 6 is specced for. Re-derive this if " +
                 "the tick rate or damage changes.")]
        [SerializeField] private int health = 3;

        [Header("Movement variety - avoids every unit looking identical")]
        [SerializeField] private float rotationSpeed = 8f;
        [SerializeField] private float speedVarianceMin = 0.85f;
        [SerializeField] private float speedVarianceMax = 1.15f;

        // Offsets the arrival point slightly so a group deployed together doesn't converge on
        // one exact spot single-file. Re-sampled onto the NavMesh so it never lands off-mesh.
        [SerializeField] private float arrivalJitterRadius = 0.1f;

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

        [SerializeField] private int attackDamage = 1;

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
        private bool hasRallyWaypoint;
        private bool speedVarianceApplied;
        private bool hasTarget;

        // Health is tracked as a float so the cover multiplier can be a genuine fraction. Authoring
        // stays an int (an Inspector "3 hit points" is readable; "3.0" invites false precision), but
        // halving integer damage would floor to zero and make cover total immunity by accident.
        private float currentHealth;
        private bool dying;

        private float engagementRadius;
        private float navMeshSampleDistance;
        private float attackTimer;
        private float recoveryRemaining;
        private SiegeUnit duelOpponent;

        public NavMeshAgent Agent => agent;

        /// <summary>Which side this unit fights for. Set by whoever spawns it, before SetTarget.</summary>
        public Team Team { get; private set; } = Team.Player;

        /// <summary>True while diverting to a rally waypoint rather than heading for the base.</summary>
        public bool IsRallying => hasRallyWaypoint;

        /// <summary>
        /// True while locked in a duel. This is the frontage cap: a unit already fighting cannot be
        /// picked as anyone else's target, so damage never concentrates.
        /// </summary>
        public bool IsEngaged => duelOpponent != null;

        /// <summary>False once the unit has started dying, so nothing targets or damages a corpse.</summary>
        public bool IsAlive => !dying;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            animator = GetComponent<UnitAnimator>();

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

            arrivalDistance = arrivalDistanceFraction * boardLength;
            arrivalJitterRadius = arrivalJitterFraction * boardLength;
            engagementRadius = engagementRadiusFraction * boardLength;
            navMeshSampleDistance = navMeshSampleFraction * boardLength;

            if (agent == null) return;

            agent.speed = boardLength / Mathf.Max(boardCrossingSeconds, 0.1f);
            agent.acceleration = agent.speed * 4f;
            // Must stay under arrivalDistance, or the agent parks itself before Update ever counts
            // it as arrived and the unit sits next to the base doing nothing.
            agent.stoppingDistance = arrivalDistance * 0.4f;
        }

        public void SetTarget(Vector3 position, BaseHealth targetBaseHealth)
        {
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
            hasRallyWaypoint = false;
            agent.SetDestination(finalDestination);
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

            hasRallyWaypoint = true;
            agent.SetDestination(hit.position);
            return true;
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

            // A unit locked in a duel has stopped moving and is not advancing on anything - skip the
            // navigation and arrival logic entirely rather than letting a stopped agent's zeroed
            // remainingDistance be misread.
            if (IsEngaged) return;

            FaceMovementDirection();
            UpdateNavigation();
        }

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

                FightTick();
                return;
            }

            // Recovery blocks STARTING a fight, never defending one. A unit that has just won is
            // briefly vulnerable rather than briefly invincible.
            if (recoveryRemaining > 0f) return;

            SiegeUnit target = FindUnengagedEnemy();
            if (target != null) BeginDuel(target);
        }

        private bool IsDuelStillValid()
        {
            if (duelOpponent == null || !duelOpponent.IsAlive) return false;

            // Generous exit range so two units that drift slightly apart mid-fight (agent avoidance
            // nudges them) don't flicker in and out of the duel.
            float exitRadius = engagementRadius * 1.6f;
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

            StopForCombat();
            other.StopForCombat();

            // Staggered so two units that meet head-on do not land every blow on the same frame and
            // annihilate each other simultaneously - a fight should usually leave a survivor.
            attackTimer = Random.Range(0f, attackTickSeconds * 0.5f);
            other.attackTimer = Random.Range(0f, attackTickSeconds * 0.5f);
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

            if (agent != null && agent.isOnNavMesh) agent.isStopped = false;
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
            duelOpponent.TakeDamage(attackDamage, applyCover: true);
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
            // would run the arrival check against a zeroed destination.
            if (!hasTarget) return;

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

            // Reached the rally waypoint, not the base - resume the original advance.
            if (hasRallyWaypoint)
            {
                hasRallyWaypoint = false;
                agent.SetDestination(finalDestination);
                return;
            }

            if (targetBase != null)
                targetBase.TakeDamage(damageToBase);
            else
                Debug.LogWarning($"{name}: arrived but has no BaseHealth target - no damage dealt. Check SiegePhaseController.DummyBaseHealth wiring.", this);

            Destroy(gameObject);
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
