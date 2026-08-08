using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using ScrapSiege.Vantage;

namespace ScrapSiege.Siege
{
    /// <summary>
    /// The high-vantage counterpart to precise deployment.
    ///
    /// Vantage as originally specced only *penalised* standing back (looser placement) while the
    /// information gain was passive and free - so the optimal play was to glance up for a second
    /// and then stay leaned in permanently. Posture became a glance, not a stance.
    ///
    /// Rally fixes that by giving high vantage an action instead of just visibility: redirect
    /// every deployed unit through a new lane. You cannot command what you cannot see, so it is
    /// gated on being physically pulled back. Now both postures do something the player needs
    /// mid-fight and they genuinely oscillate between them.
    ///
    /// Flow: tap Rally (only interactable when high) -> arming -> tap the board -> all units
    /// divert through that point, then resume their advance.
    /// </summary>
    public class RallyController : MonoBehaviour
    {
        [SerializeField] private ARRaycastManager raycastManager;
        [SerializeField] private VantageController vantage;
        [SerializeField] private ResourceEconomy resourceEconomy;

        [Tooltip("Resource cost per rally. Free rallies make it strictly correct to spam it.")]
        [SerializeField] private int rallyCost = 1;

        [Tooltip("Seconds before Rally can be issued again, so it stays a decision rather than a spam button.")]
        [SerializeField] private float cooldownSeconds = 8f;

        [Tooltip("Supplies the board length the rally snap radius is scaled against.")]
        [SerializeField] private ScrapSiege.Core.BoardPlane boardPlane;

        [Tooltip("How far from the tapped point to search for a walkable rally waypoint, as a " +
                 "fraction of board length. The old absolute 0.25m was 42% of a 0.60m board, so a " +
                 "rally could land almost anywhere regardless of where the player actually tapped.")]
        [SerializeField] private float navMeshSnapFraction = 0.06f;

        [Tooltip("Used only when no board has been established (the legacy scan/Fortify flow).")]
        [SerializeField] private float navMeshSnapFallback = 0.08f;

        [Header("Events")]
        /// <summary>True while waiting for the player to tap a destination.</summary>
        public UnityEvent<bool> OnArmedChanged;

        /// <summary>Fires with the number of units redirected, so the HUD can confirm the order landed.</summary>
        public UnityEvent<int> OnRallyIssued;

        /// <summary>Remaining cooldown as 0..1 (1 = just used, 0 = ready).</summary>
        public UnityEvent<float> OnCooldownChanged;

        private readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();
        private bool armed;
        private float cooldownRemaining;
        private float lastBroadcastCooldown = -1f;

        /// <summary>True when vantage, cooldown and resources all permit a rally right now.</summary>
        public bool CanIssue =>
            enabled
            && vantage != null
            && vantage.IsRallyReady
            && cooldownRemaining <= 0f
            && (resourceEconomy == null || resourceEconomy.CurrentResources >= rallyCost);

        private void Awake()
        {
            // Mirrors UnitDeploymentController: Siege turns this on, nothing before it.
            enabled = false;

            if (raycastManager == null) Debug.LogError("RallyController: Raycast Manager is not assigned - rally taps can never hit the board.", this);
            if (vantage == null) Debug.LogError("RallyController: Vantage Controller is not assigned - the rally height gate cannot be evaluated, so Rally will never unlock.", this);
        }

        private void OnDisable()
        {
            // Never leave the HUD stuck showing an armed reticle after Siege ends.
            SetArmed(false);
        }

        private void Update()
        {
            TickCooldown();

            if (!armed) return;

            // Losing the posture mid-order cancels it - otherwise the player could arm high, lean
            // back in, and still issue a board-wide command they can no longer see to give.
            if (vantage != null && !vantage.IsRallyReady)
            {
                SetArmed(false);
                return;
            }

            var touch = Touchscreen.current?.primaryTouch;
            if (touch == null || !touch.press.wasPressedThisFrame) return;

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(touch.touchId.ReadValue()))
                return;

            Vector2 screenPos = touch.position.ReadValue();
            if (!raycastManager.Raycast(screenPos, hits, TrackableType.PlaneWithinPolygon)) return;

            IssueRally(hits[0].pose.position);
        }

        private void TickCooldown()
        {
            if (cooldownRemaining > 0f)
            {
                cooldownRemaining = Mathf.Max(0f, cooldownRemaining - Time.deltaTime);

                float normalized = cooldownSeconds > 0f ? cooldownRemaining / cooldownSeconds : 0f;
                if (!Mathf.Approximately(normalized, lastBroadcastCooldown))
                {
                    lastBroadcastCooldown = normalized;
                    OnCooldownChanged?.Invoke(normalized);
                }
            }
        }

        /// <summary>Wire to the Rally button. Arms the order; the next board tap places it.</summary>
        public void ToggleArmed()
        {
            if (armed)
            {
                SetArmed(false);
                return;
            }

            if (!CanIssue)
            {
                Debug.Log($"RallyController: cannot arm - rallyReady={(vantage != null && vantage.IsRallyReady)} cooldown={cooldownRemaining:0.0}s resources={(resourceEconomy != null ? resourceEconomy.CurrentResources : -1)}");
                return;
            }

            SetArmed(true);
        }

        private void IssueRally(Vector3 worldPoint)
        {
            if (!CanIssue)
            {
                SetArmed(false);
                return;
            }

            float boardLength = boardPlane != null ? boardPlane.Length : 0f;
            float snapDistance = boardLength > 0f
                ? navMeshSnapFraction * boardLength
                : ScrapSiege.Core.WorldScale.Metres(navMeshSnapFallback);

            int redirected = 0;
            foreach (var unit in SiegeUnit.Active)
            {
                if (unit == null) continue;

                // SiegeUnit.Active holds both armies. Rally is the PLAYER's board-wide order - without
                // this filter it would helpfully redirect the AI commander's attackers as well, which
                // would read as the order doing nothing (or worse, as the AI dodging).
                if (unit.Team != Team.Player) continue;
                if (!unit.IsAlive) continue;

                if (unit.RallyTo(worldPoint, snapDistance)) redirected++;
            }

            SetArmed(false);

            // Only charge for an order that actually moved something. A rally tapped onto an
            // unreachable spot with no units alive should not silently eat a resource.
            if (redirected == 0)
            {
                Debug.Log("RallyController: rally point unreachable or no units deployed - order not charged.");
                return;
            }

            if (resourceEconomy != null) resourceEconomy.TrySpend(rallyCost);

            cooldownRemaining = cooldownSeconds;
            lastBroadcastCooldown = 1f;
            OnCooldownChanged?.Invoke(1f);
            OnRallyIssued?.Invoke(redirected);
        }

        private void SetArmed(bool value)
        {
            if (armed == value) return;

            armed = value;
            OnArmedChanged?.Invoke(armed);
        }
    }
}
