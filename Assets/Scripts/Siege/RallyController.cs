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

        [Tooltip("The authored-level flow. When assigned, rally taps are intersected against the " +
                 "board's own transform instead of an ARCore plane - see ResolveTapPoint.")]
        [SerializeField] private ScrapSiege.Levels.LevelMatchController levelMatch;

        [Tooltip("Camera the rally tap is cast from. Optional - falls back to Camera.main.")]
        [SerializeField] private Camera rallyCamera;

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

        /// <summary>Fires when the scope changes, with a label the HUD can show verbatim.</summary>
        public UnityEvent<string> OnScopeChanged = new UnityEvent<string>();

        private readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();
        private bool armed;
        private int consumedTapFrame = -1;

        // null == every unit on the board. Any other value restricts the order to one class.
        private UnitClass scopeClass;
        private float cooldownRemaining;
        private float lastBroadcastCooldown = -1f;

        /// <summary>
        /// True when a board tap this frame belongs to Rally and nothing else may act on it.
        ///
        /// <para><b>Why the frame number and not just <c>armed</c>.</b> This and
        /// <see cref="UnitDeploymentController"/> read the same touch independently from their own
        /// <c>Update</c>, so an armed rally tap was being answered twice - the army redirected AND a
        /// unit was deployed and paid for on the same tap (reported from device, 2026-08-13).
        /// Checking <c>armed</c> alone does not fix it, because Rally clears <c>armed</c> inside the
        /// very Update that consumes the tap: if deployment's Update happens to run second in
        /// Unity's script order, it would see <c>armed == false</c> and deploy anyway. That would be
        /// a bug that appears or disappears with script execution order, which is the worst kind to
        /// own. Recording the frame makes the claim true for the whole frame regardless of order.</para>
        /// </summary>
        public bool ClaimsBoardTap => armed || consumedTapFrame == Time.frameCount;

        /// <summary>True while waiting for the player to tap a rally destination.</summary>
        public bool IsArmed => armed;

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

            if (raycastManager == null && levelMatch == null)
                Debug.LogError("RallyController: neither Raycast Manager nor Level Match is assigned - rally taps can never resolve to a point on the board.", this);
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

            // Claimed the moment the tap is read, before it is known whether it resolves to a point.
            // A tap that misses the board while armed still belongs to Rally - letting deployment
            // pick it up instead would spawn a unit somewhere the player was aiming a manoeuvre at.
            consumedTapFrame = Time.frameCount;

            Vector2 screenPos = touch.position.ReadValue();
            if (!TryResolveTapPoint(screenPos, out Vector3 rallyPoint)) return;

            IssueRally(rallyPoint);
        }

        /// <summary>
        /// Intersects the tap against the placed board, exactly as
        /// <see cref="UnitDeploymentController.TryResolveTapPoint"/> does.
        ///
        /// <para>This was the same latent bug the deploy path was fixed for on 2026-08-10: requiring
        /// a <c>PlaneWithinPolygon</c> AR hit while <see cref="ScrapSiege.Levels.BoardPlacementController"/>
        /// happily places a board off feature points alone. On the Tab S6 Lite, which tracks fine but
        /// never promotes anything to a plane, that combination means every rally tap is silently
        /// discarded for the whole match - the button arms, the player taps, and nothing at all
        /// happens. Fixing deploy and leaving rally on the old path would have left half the bug in
        /// place.</para>
        /// </summary>
        private bool TryResolveTapPoint(Vector2 screenPos, out Vector3 point)
        {
            Transform board = levelMatch != null ? levelMatch.BoardRoot : null;
            Camera cam = ResolveCamera();

            if (board != null && cam != null)
            {
                var plane = new Plane(board.up, board.position);
                Ray ray = cam.ScreenPointToRay(screenPos);
                if (plane.Raycast(ray, out float distance))
                {
                    Vector3 hit = ray.GetPoint(distance);
                    Vector3 local = board.InverseTransformPoint(hit);
                    if (Mathf.Abs(local.x) <= 0.5f && Mathf.Abs(local.z) <= 0.5f)
                    {
                        point = hit;
                        return true;
                    }
                }

                point = default;
                return false;
            }

            if (raycastManager != null && raycastManager.Raycast(screenPos, hits, TrackableType.PlaneWithinPolygon))
            {
                point = hits[0].pose.position;
                return true;
            }

            point = default;
            return false;
        }

        private Camera ResolveCamera()
        {
            if (rallyCamera == null) rallyCamera = Camera.main;
            return rallyCamera;
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

        /// <summary>
        /// Restricts the order to one class, or to everything when passed null.
        ///
        /// <para><b>Why this exists.</b> A board-wide rally is a blunt instrument: every unit
        /// diverts, including the ones already doing exactly what you wanted. That made the order a
        /// panic button rather than a manoeuvre, and it collapsed the point of having several unit
        /// classes at once - you could not pull a screening line back without also pulling the
        /// saboteur that was two steps from the enemy base. Scoping it turns Rally into the thing
        /// the high vantage is actually for: seeing the whole board and moving one part of it.</para>
        ///
        /// <para>Scope deliberately does NOT gate <see cref="CanIssue"/>. An order that redirects
        /// nothing is refunded by the existing "not charged" path, so a mis-scoped rally costs the
        /// player a tap rather than a resource.</para>
        /// </summary>
        public void SetScope(UnitClass unitClass)
        {
            if (scopeClass == unitClass) return;

            scopeClass = unitClass;
            OnScopeChanged?.Invoke(ScopeLabel);
        }

        /// <summary>
        /// Label for the HUD - "ALL" or the class's own name, upper-cased.
        ///
        /// <para>Uses <c>displayName</c> rather than <c>shortLabel</c>: the scope chip is 300px wide
        /// on the 1920 reference canvas, so "RALLY · MARKSMAN" fits comfortably, and a scope you
        /// have to decode ("RALLY · MKS") defeats the point of a control whose whole job is telling
        /// you what your next order will hit.</para>
        /// </summary>
        public string ScopeLabel => scopeClass != null
            ? (string.IsNullOrWhiteSpace(scopeClass.displayName)
                ? scopeClass.shortLabel
                : scopeClass.displayName.ToUpperInvariant())
            : "ALL";

        /// <summary>What the order currently applies to. Null means every player unit.</summary>
        public UnitClass Scope => scopeClass;

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

            ScrapSiege.Audio.GameAudio.Play(ScrapSiege.Audio.Sfx.Rally);

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

                // Scope filter. An emplacement is excluded whatever the scope says - it cannot move,
                // and counting it as "redirected" would charge for an order it never carried out.
                if (unit.IsStationary) continue;
                if (scopeClass != null && unit.Class != scopeClass) continue;

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
