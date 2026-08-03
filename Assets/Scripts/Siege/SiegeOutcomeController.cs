using UnityEngine;
using UnityEngine.Events;

namespace ScrapSiege.Siege
{
    /// <summary>
    /// Closes the Siege loop: watches the dummy base's health and, once it's destroyed, stops
    /// the resource/deploy systems and raises a Win event. This is the "Aftermath" phase from
    /// plan.md's match structure in placeholder form - no cinematic zoom yet, just a clean stop
    /// and a UI hook. There is no Lose condition yet because nothing currently damages the
    /// player back (no real opponent until Week 3 Cloud Anchor sync); add one once something can.
    /// </summary>
    public class SiegeOutcomeController : MonoBehaviour
    {
        [SerializeField] private ResourceEconomy resourceEconomy;
        [SerializeField] private UnitDeploymentController deploymentController;

        public UnityEvent OnPlayerWon;

        /// <summary>Wire SiegePhaseController to call this once the dummy base's BaseHealth exists.</summary>
        public void WatchBase(BaseHealth baseHealth)
        {
            baseHealth.OnBaseDestroyed.AddListener(HandleBaseDestroyed);
        }

        private void HandleBaseDestroyed()
        {
            // A missing Inspector reference here must never block OnPlayerWon from firing - log
            // it loudly and keep going instead of a silent no-op or an uncaught exception (this
            // exact gap - resourceEconomy/deploymentController unassigned - swallowed a real win
            // once already).
            if (resourceEconomy != null) resourceEconomy.enabled = false;
            else Debug.LogError("SiegeOutcomeController: Resource Economy is not assigned - it won't stop ticking after a win.", this);

            if (deploymentController != null) deploymentController.enabled = false;
            else Debug.LogError("SiegeOutcomeController: Deployment Controller is not assigned - deploys won't stop after a win.", this);

            OnPlayerWon?.Invoke();
        }
    }
}
