using UnityEngine;
using UnityEngine.AI;
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
        [SerializeField] private float detectionRadius = 0.5f;

        [Tooltip("Total width of the covered wedge in degrees, bisected by this object's forward. " +
                 "360 restores the old full-circle behaviour.")]
        [Range(20f, 360f)]
        [SerializeField] private float facingArcDegrees = 150f;

        [SerializeField] private float tickInterval = 0.5f;
        [SerializeField] private int damagePerTick = 1;
        [SerializeField] private float navMeshSampleDistance = 0.1f;

        /// <summary>Read by SentryArcVisualizer so the drawn wedge always matches the real rule.</summary>
        public float DetectionRadius => detectionRadius;

        /// <summary>Read by SentryArcVisualizer so the drawn wedge always matches the real rule.</summary>
        public float FacingArcDegrees => facingArcDegrees;

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
                if (!IsInArc(unit.transform.position)) continue;
                if (IsInCoverLane(unit.transform.position)) continue;

                unit.TakeDamage(damagePerTick);
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

        private bool IsInCoverLane(Vector3 position)
        {
            if (!NavMesh.SamplePosition(position, out NavMeshHit hit, navMeshSampleDistance, NavMesh.AllAreas))
                return false;

            return (hit.mask & NavMeshAreas.CoverAreaMask) != 0;
        }
    }
}
