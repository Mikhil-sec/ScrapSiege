using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using ScrapSiege.Core;

namespace ScrapSiege.Siege
{
    /// <summary>
    /// Drives a NavMeshAgent toward a deployed destination and damages the base on arrival.
    /// Also has its own health so GarrisonSentry can chip it down before it gets there.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class SiegeUnit : MonoBehaviour
    {
        // Fallbacks in REAL metres, overwritten by ConfigureForBoard at spawn. They only matter if a
        // unit is ever created without a board, so they are converted rather than left raw.
        [SerializeField] private float arrivalDistance = 0.15f;
        [SerializeField] private int damageToBase = 1;
        [SerializeField] private int health = 2;

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

        private static readonly List<SiegeUnit> active = new List<SiegeUnit>();

        /// <summary>All currently-deployed units - lets GarrisonSentry find nearby targets without needing colliders on the unit prefab.</summary>
        public static IReadOnlyList<SiegeUnit> Active => active;

        private NavMeshAgent agent;
        private BaseHealth targetBase;

        // Where this unit is ultimately headed. Kept separate from agent.destination because a
        // Rally order temporarily overrides the destination with a waypoint, and the unit has to
        // know what to resume toward once it gets there.
        private Vector3 finalDestination;
        private bool hasRallyWaypoint;
        private bool speedVarianceApplied;

        public NavMeshAgent Agent => agent;

        /// <summary>True while diverting to a rally waypoint rather than heading for the base.</summary>
        public bool IsRallying => hasRallyWaypoint;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();

            arrivalDistance = WorldScale.Metres(arrivalDistance);
            arrivalJitterRadius = WorldScale.Metres(arrivalJitterRadius);

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

        private void OnDisable() => active.Remove(this);

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

        public void TakeDamage(int amount)
        {
            health -= amount;
            if (health <= 0)
                Destroy(gameObject);
        }

        private void Update()
        {
            FaceMovementDirection();

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
