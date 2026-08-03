using UnityEngine;
using UnityEngine.Events;

namespace ScrapSiege.Siege
{
    /// <summary>
    /// Timer-based resource tick (plan.md's stated fallback default over explore-to-earn).
    /// Disabled until Siege begins; SiegePhaseController turns this on.
    /// </summary>
    public class ResourceEconomy : MonoBehaviour
    {
        [SerializeField] private int startingResources = 3;

        // Capped so idle time doesn't stockpile unlimited deployments - tuned via playtesting.
        [SerializeField] private int maxResources = 10;
        [SerializeField] private float tickIntervalSeconds = 2f;
        [SerializeField] private int tickAmount = 1;

        public UnityEvent<int> OnResourceCountChanged;

        public int CurrentResources { get; private set; }

        private void Awake()
        {
            // Must not tick/spend during Fortify - only SiegePhaseController.StartSiege() turns
            // this on. Enforced here rather than relying on the Inspector checkbox being unchecked.
            enabled = false;
        }

        private void OnEnable()
        {
            CurrentResources = startingResources;
            OnResourceCountChanged?.Invoke(CurrentResources);
            InvokeRepeating(nameof(Tick), tickIntervalSeconds, tickIntervalSeconds);
        }

        private void OnDisable()
        {
            CancelInvoke(nameof(Tick));
        }

        private void Tick()
        {
            if (CurrentResources >= maxResources) return;

            CurrentResources = Mathf.Min(CurrentResources + tickAmount, maxResources);
            OnResourceCountChanged?.Invoke(CurrentResources);
        }

        public bool TrySpend(int amount)
        {
            if (CurrentResources < amount) return false;

            CurrentResources -= amount;
            OnResourceCountChanged?.Invoke(CurrentResources);
            return true;
        }
    }
}
