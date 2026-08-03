using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ScrapSiege.Siege
{
    /// <summary>
    /// Drives a NavMeshAgent toward a deployed destination and damages the base on arrival.
    /// Also has its own health so GarrisonSentry can chip it down before it gets there.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class SiegeUnit : MonoBehaviour
    {
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

        private static readonly List<SiegeUnit> active = new List<SiegeUnit>();

        /// <summary>All currently-deployed units - lets GarrisonSentry find nearby targets without needing colliders on the unit prefab.</summary>
        public static IReadOnlyList<SiegeUnit> Active => active;

        private NavMeshAgent agent;
        private BaseHealth targetBase;

        public NavMeshAgent Agent => agent;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
        }

        private void OnEnable() => active.Add(this);

        private void OnDisable() => active.Remove(this);

        public void SetTarget(Vector3 position, BaseHealth targetBaseHealth)
        {
            targetBase = targetBaseHealth;
            agent.speed *= Random.Range(speedVarianceMin, speedVarianceMax);
            agent.SetDestination(JitteredDestination(position));
        }

        private Vector3 JitteredDestination(Vector3 position)
        {
            if (arrivalJitterRadius <= 0f) return position;

            Vector2 offset = Random.insideUnitCircle * arrivalJitterRadius;
            Vector3 jittered = position + new Vector3(offset.x, 0f, offset.y);

            if (NavMesh.SamplePosition(jittered, out NavMeshHit hit, arrivalJitterRadius + 0.1f, NavMesh.AllAreas))
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
            if (agent.remainingDistance > arrivalDistance) return;

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
