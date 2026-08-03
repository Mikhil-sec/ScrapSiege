using UnityEngine;
using UnityEngine.AI;
using ScrapSiege.Terrain;

namespace ScrapSiege.Siege
{
    /// <summary>
    /// Stationed at a chokepoint by MusterPhaseController. Periodically damages any deployed
    /// SiegeUnit within range that is NOT currently standing in a CoverLane NavMesh area - this
    /// is what gives the route-variety deploy choice (Direct vs. Covered) actual stakes instead
    /// of just being cosmetic path shapes.
    /// </summary>
    public class GarrisonSentry : MonoBehaviour
    {
        [SerializeField] private float detectionRadius = 0.5f;
        [SerializeField] private float tickInterval = 0.5f;
        [SerializeField] private int damagePerTick = 1;
        [SerializeField] private float navMeshSampleDistance = 0.1f;

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
                if (Vector3.Distance(unit.transform.position, transform.position) > detectionRadius) continue;
                if (IsInCoverLane(unit.transform.position)) continue;

                unit.TakeDamage(damagePerTick);
            }
        }

        private bool IsInCoverLane(Vector3 position)
        {
            if (!NavMesh.SamplePosition(position, out NavMeshHit hit, navMeshSampleDistance, NavMesh.AllAreas))
                return false;

            return (hit.mask & NavMeshAreas.CoverAreaMask) != 0;
        }
    }
}
