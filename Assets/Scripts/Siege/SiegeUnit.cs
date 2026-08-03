using UnityEngine;
using UnityEngine.AI;

namespace ScrapSiege.Siege
{
    /// <summary>
    /// Drives a NavMeshAgent toward a deployed destination. Minimal "arrived" feedback only -
    /// no combat/health system yet, just enough to visually confirm the deploy-path-arrive loop.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class SiegeUnit : MonoBehaviour
    {
        [SerializeField] private float arrivalDistance = 0.15f;

        private NavMeshAgent agent;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
        }

        public void SetDestination(Vector3 target)
        {
            agent.SetDestination(target);
        }

        private void Update()
        {
            if (agent.pathPending) return;
            if (agent.remainingDistance > arrivalDistance) return;

            Destroy(gameObject);
        }
    }
}
