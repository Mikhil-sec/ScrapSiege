using UnityEngine;
using UnityEngine.Events;
using ScrapSiege.Core;

namespace ScrapSiege.Vantage
{
    /// <summary>
    /// Mechanic 1 (plan.md Section 4): the phone's physical height above the board is a
    /// continuously-read gameplay input. There is no UI toggle - posture *is* the control.
    ///
    /// Reading height (rather than, say, camera-to-board distance) is deliberate: it is what
    /// plan.md specifies, it stays correct no matter where over the board the player is standing,
    /// and it degrades sanely when tracking is poor. Everything derived from it is exposed as a
    /// property plus a change event, so the HUD never has to poll.
    /// </summary>
    public class VantageController : MonoBehaviour
    {
        [SerializeField] private Camera arCamera;
        [SerializeField] private BoardPlane board;

        [Header("Posture band (metres above the board surface)")]
        [Tooltip("At or below this height the player is fully 'leaned in': placement is pixel-tight.")]
        [SerializeField] private float leanedInHeight = 0.20f;

        [Tooltip("At or above this height the player is fully 'pulled back': maximum scatter, Rally available.")]
        [SerializeField] private float pulledBackHeight = 0.65f;

        [Header("Deploy scatter (metres)")]
        [SerializeField] private float minScatterRadius = 0.005f;
        [SerializeField] private float maxScatterRadius = 0.10f;

        [Header("Rally gate")]
        [Tooltip("Vantage (0..1) at which the Rally order unlocks. You cannot command what you cannot see.")]
        [Range(0f, 1f)]
        [SerializeField] private float rallyThreshold = 0.6f;

        [Tooltip("Dead-band around the threshold so the Rally button doesn't strobe when held at the boundary.")]
        [SerializeField] private float rallyHysteresis = 0.08f;

        [Header("Feel")]
        [Tooltip("Higher = snappier response, lower = smoother. Handheld camera height is noisy; " +
                 "without smoothing, tight placement feels random instead of skilful.")]
        [SerializeField] private float smoothingPerSecond = 8f;

        [Header("Events")]
        /// <summary>Current vantage, 0 = leaned in, 1 = pulled back. Fires every frame it changes meaningfully.</summary>
        public UnityEvent<float> OnVantageChanged;

        /// <summary>Fires only on transitions, so the HUD can react without polling.</summary>
        public UnityEvent<bool> OnRallyAvailabilityChanged;

        /// <summary>0 = leaned in (precise), 1 = pulled back (imprecise, full board).</summary>
        public float Vantage01 { get; private set; }

        /// <summary>Metres of random offset applied to a deploy tap at the current posture.</summary>
        public float ScatterRadius => VantageMath.ScatterRadius(Vantage01, minScatterRadius, maxScatterRadius);

        /// <summary>True when the player is high enough to issue a Rally order.</summary>
        public bool IsRallyReady { get; private set; }

        private const float ChangeEpsilon = 0.002f;
        private float lastBroadcastVantage = -1f;
        private bool initialised;

        private void Awake()
        {
            if (arCamera == null)
            {
                arCamera = Camera.main;
                if (arCamera == null)
                    Debug.LogError("VantageController: AR Camera is not assigned and no MainCamera exists - vantage will stay at 0 (always fully precise).", this);
            }

            if (board == null)
                Debug.LogError("VantageController: Board Plane is not assigned - vantage cannot know the table height and will stay at 0.", this);
        }

        private void Update()
        {
            if (arCamera == null || board == null) return;

            float raw = VantageMath.Normalized(
                arCamera.transform.position.y,
                board.Height,
                leanedInHeight,
                pulledBackHeight);

            // Snap on the first frame rather than easing up from 0, otherwise the player starts
            // every match with a second of artificially perfect precision.
            Vantage01 = initialised
                ? VantageMath.Smooth(Vantage01, raw, smoothingPerSecond, Time.deltaTime)
                : raw;
            initialised = true;

            if (Mathf.Abs(Vantage01 - lastBroadcastVantage) > ChangeEpsilon)
            {
                lastBroadcastVantage = Vantage01;
                OnVantageChanged?.Invoke(Vantage01);
            }

            bool rallyReady = VantageMath.EvaluateRallyReady(Vantage01, IsRallyReady, rallyThreshold, rallyHysteresis);
            if (rallyReady != IsRallyReady)
            {
                IsRallyReady = rallyReady;
                OnRallyAvailabilityChanged?.Invoke(IsRallyReady);
            }
        }

        /// <summary>
        /// Applies the current posture's imprecision to a deploy point. Offsets on the board plane
        /// only - scattering vertically would push taps off the NavMesh for no design reason.
        /// </summary>
        public Vector3 ApplyScatter(Vector3 tapPoint)
        {
            float radius = ScatterRadius;
            if (radius <= 0f) return tapPoint;

            Vector2 offset = Random.insideUnitCircle * radius;
            return tapPoint + new Vector3(offset.x, 0f, offset.y);
        }
    }
}
