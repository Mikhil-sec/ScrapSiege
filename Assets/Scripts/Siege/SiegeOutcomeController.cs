using UnityEngine;
using UnityEngine.Events;

namespace ScrapSiege.Siege
{
    /// <summary>
    /// Closes the Siege loop: watches both bases and, once either is destroyed, stops the
    /// resource/deploy/AI systems and raises the matching outcome event. This is the "Aftermath"
    /// phase from plan.md's match structure in placeholder form - no cinematic zoom yet, just a
    /// clean stop and a UI hook.
    ///
    /// <para>The Lose side arrived with the AI commander. It had been deferred for a genuine reason -
    /// nothing could damage the player back - but that reason expired the moment the AI started
    /// deploying real attackers, and it was a hard dependency rather than a nice-to-have: without it
    /// an AI push that reached the player's base would land on nothing and the AI's entire purpose
    /// would be unobservable. <c>LevelBuilder</c> has been building a real
    /// <see cref="BaseHealth"/> for the player base the whole time; nothing ever watched it.</para>
    /// </summary>
    public class SiegeOutcomeController : MonoBehaviour
    {
        [SerializeField] private ResourceEconomy resourceEconomy;
        [SerializeField] private UnitDeploymentController deploymentController;

        [Tooltip("Optional - the high-vantage Rally order. Stopped alongside deployment so no order " +
                 "can be issued after the match is decided.")]
        [SerializeField] private RallyController rallyController;

        public UnityEvent OnPlayerWon;
        public UnityEvent OnPlayerLost;

        private bool decided;

        /// <summary>
        /// Extra behaviours to switch off when the match ends - the AI commander registers itself
        /// here. A list rather than a serialized field because the commander is only present on
        /// levels that opt into it, so an Inspector slot would be null on most levels and the null
        /// would be indistinguishable from a wiring mistake.
        /// </summary>
        private readonly System.Collections.Generic.List<Behaviour> stopOnEnd
            = new System.Collections.Generic.List<Behaviour>();

        public void RegisterStopOnEnd(Behaviour behaviour)
        {
            if (behaviour != null && !stopOnEnd.Contains(behaviour)) stopOnEnd.Add(behaviour);
        }

        /// <summary>Wire SiegePhaseController/LevelMatchController to call this with the ENEMY base.</summary>
        public void WatchBase(BaseHealth baseHealth)
        {
            if (baseHealth == null)
            {
                Debug.LogError("SiegeOutcomeController.WatchBase: enemy base health is null - the win condition can never fire.", this);
                return;
            }

            baseHealth.OnBaseDestroyed.AddListener(HandleEnemyBaseDestroyed);
        }

        /// <summary>Wire with the PLAYER's base, so an AI push that gets through actually ends the match.</summary>
        public void WatchPlayerBase(BaseHealth baseHealth)
        {
            if (baseHealth == null)
            {
                Debug.LogError("SiegeOutcomeController.WatchPlayerBase: player base health is null - the lose condition can never fire.", this);
                return;
            }

            baseHealth.OnBaseDestroyed.AddListener(HandlePlayerBaseDestroyed);
        }

        /// <summary>
        /// Starts the match clock. Called by <see cref="ScrapSiege.Levels.LevelMatchController"/> at
        /// the moment the siege goes live, deliberately NOT at scene load - the player spends an
        /// unbounded amount of time scanning a table and placing the board before that, and grading
        /// them on their AR setup would make the time star a measure of their lighting conditions.
        /// </summary>
        public void BeginMatch() => MatchStats.Begin();

        private void HandleEnemyBaseDestroyed()
        {
            if (!EndMatch()) return;
            ScrapSiege.Audio.GameAudio.Play(ScrapSiege.Audio.Sfx.Victory);
            OnPlayerWon?.Invoke();
        }

        private void HandlePlayerBaseDestroyed()
        {
            if (!EndMatch()) return;
            ScrapSiege.Audio.GameAudio.Play(ScrapSiege.Audio.Sfx.Defeat);
            OnPlayerLost?.Invoke();
        }

        /// <summary>
        /// Stops the match systems. Returns false if the match was already decided, which guards the
        /// genuine race now that two bases can be destroyed: a player unit and an AI unit can land
        /// killing blows on the same frame, and without this both panels would be raised at once.
        /// </summary>
        private bool EndMatch()
        {
            if (decided) return false;
            decided = true;

            // Stopped before the outcome events fire, so the HUD reads a frozen time rather than one
            // that keeps ticking while the player looks at the card.
            MatchStats.Stop();

            // A missing Inspector reference here must never block the outcome event from firing - log
            // it loudly and keep going instead of a silent no-op or an uncaught exception (this
            // exact gap - resourceEconomy/deploymentController unassigned - swallowed a real win
            // once already).
            if (resourceEconomy != null) resourceEconomy.enabled = false;
            else Debug.LogError("SiegeOutcomeController: Resource Economy is not assigned - it won't stop ticking after the match ends.", this);

            if (deploymentController != null) deploymentController.enabled = false;
            else Debug.LogError("SiegeOutcomeController: Deployment Controller is not assigned - deploys won't stop after the match ends.", this);

            if (rallyController != null) rallyController.enabled = false;

            foreach (var behaviour in stopOnEnd)
                if (behaviour != null) behaviour.enabled = false;

            return true;
        }
    }
}
