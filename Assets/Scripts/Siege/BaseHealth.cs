using UnityEngine;
using UnityEngine.Events;

namespace ScrapSiege.Siege
{
    /// <summary>
    /// Generic hit-point tracker. Used on the dummy base now; the same component works for a
    /// real opponent base once Week 3 sync exists, and for the player's own base later.
    /// </summary>
    public class BaseHealth : MonoBehaviour
    {
        [SerializeField] private int maxHealth = 20;

        public UnityEvent<int> OnHealthChanged;
        public UnityEvent OnBaseDestroyed;

        public int CurrentHealth { get; private set; }
        public bool IsDestroyed { get; private set; }

        private void Awake()
        {
            CurrentHealth = maxHealth;
        }

        /// <summary>
        /// Overrides the prefab's health for this instance. Authored levels tune base HP per map
        /// (a short skirmish and a long siege want different numbers off the same prefab), so the
        /// value has to be settable after Instantiate rather than baked into the asset.
        /// </summary>
        public void ResetTo(int newMaxHealth)
        {
            maxHealth = Mathf.Max(1, newMaxHealth);
            CurrentHealth = maxHealth;
            IsDestroyed = false;
            OnHealthChanged?.Invoke(CurrentHealth);
        }

        public void TakeDamage(int amount)
        {
            if (IsDestroyed) return;

            CurrentHealth = Mathf.Max(0, CurrentHealth - amount);
            OnHealthChanged?.Invoke(CurrentHealth);
            ScrapSiege.Audio.GameAudio.Play(ScrapSiege.Audio.Sfx.BaseHit);
            Debug.Log($"{name}: took {amount} damage, {CurrentHealth}/{maxHealth} remaining.", this);

            if (CurrentHealth == 0)
            {
                IsDestroyed = true;
                Debug.Log($"{name}: destroyed, firing OnBaseDestroyed.", this);
                OnBaseDestroyed?.Invoke();
            }
        }
    }
}
