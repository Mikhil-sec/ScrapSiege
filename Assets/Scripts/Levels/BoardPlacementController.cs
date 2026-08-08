using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using ScrapSiege.Core;

namespace ScrapSiege.Levels
{
    /// <summary>
    /// The phase that replaces Fortify: drop an authored board onto the locked plane, then fit it
    /// to the real table before committing.
    ///
    /// Tap to place, one finger to drag, two fingers to pinch-scale and twist-rotate, then Confirm.
    /// Direct manipulation rather than sliders, because the player is judging the fit against their
    /// own table by eye and any indirection makes that harder.
    ///
    /// Deliberately does NOT require a large, high-quality plane: it only needs a single raycast
    /// hit to place the board, and falls back to estimated planes and feature points. Plane
    /// detection is this project's proven weak point (plan.md Section 9), so the placement step is
    /// built to work off the smallest possible seed.
    /// </summary>
    public class BoardPlacementController : MonoBehaviour
    {
        [SerializeField] private ARRaycastManager raycastManager;
        [SerializeField] private Camera arCamera;
        [SerializeField] private BoardPlane boardPlane;

        [Tooltip("Transform every level object is built under. Its scale is the board's length in metres.")]
        [SerializeField] private Transform boardRoot;

        // Authored in REAL metres - "a 60cm board" is the meaningful unit here - and converted to
        // the scaled AR world through WorldScale at the point of use. Left unconverted, the default
        // board would land 60cm long in Unity units, i.e. 12cm of actual table.
        [Header("Size limits (board length, REAL metres)")]
        [SerializeField] private float minBoardLength = 0.25f;
        [SerializeField] private float maxBoardLength = 1.60f;
        [SerializeField] private float defaultBoardLength = 0.60f;

        private float MinBoardLengthWorld => WorldScale.Metres(minBoardLength);
        private float MaxBoardLengthWorld => WorldScale.Metres(maxBoardLength);
        private float DefaultBoardLengthWorld => WorldScale.Metres(defaultBoardLength);

        [Header("Feel")]
        [SerializeField] private float rotationSensitivity = 1f;

        [Header("Footprint preview")]
        [Tooltip("Any material using the active render pipeline - the outline is instanced from it.")]
        [SerializeField] private Material outlineMaterial;

        [SerializeField] private Color outlineColor = new Color(1f, 0.54f, 0.24f);
        [SerializeField] private float outlineWidth = 0.006f;

        [Tooltip("Lifts the outline off the surface so it doesn't z-fight with the table.")]
        [SerializeField] private float outlineLift = 0.002f;

        [Header("Events")]
        public UnityEvent OnPlacementStarted;

        /// <summary>True once the board has been dropped at least once, so Confirm can enable.</summary>
        public UnityEvent<bool> OnBoardPlacedChanged;

        /// <summary>Fires when the player confirms the fit - the level is then locked in.</summary>
        public UnityEvent OnPlacementConfirmed;

        public bool HasPlacedBoard { get; private set; }
        public Transform BoardRoot => boardRoot;

        private readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();
        private float currentLength;

        // Width/length ratio of the level being placed, so the preview matches the real footprint.
        private float boardAspect = 0.6f;
        private LineRenderer outline;
        private Material outlineInstance;

        // Two-finger gesture state, captured on the frame the second finger lands.
        private bool gestureActive;
        private float gestureStartPinchDistance;
        private float gestureStartLength;
        private float gestureStartTwistAngle;
        private float gestureStartYaw;

        private void Awake()
        {
            enabled = false; // PlaneLockController turns this on once a plane is locked.

            if (raycastManager == null) Debug.LogError("BoardPlacementController: Raycast Manager is not assigned - the board can never be placed.", this);
            if (boardRoot == null) Debug.LogError("BoardPlacementController: Board Root is not assigned - there is nothing to place.", this);
            if (boardPlane == null) Debug.LogError("BoardPlacementController: Board Plane is not assigned - vantage will not know the table height.", this);
            if (arCamera == null) arCamera = Camera.main;

            currentLength = DefaultBoardLengthWorld;
            if (boardRoot != null) boardRoot.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            HasPlacedBoard = false;
            OnBoardPlacedChanged?.Invoke(false);
            OnPlacementStarted?.Invoke();
        }

        private void Update()
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen == null) return;

            var touches = touchscreen.touches;
            int active = 0;
            for (int i = 0; i < touches.Count; i++)
                if (touches[i].press.isPressed) active++;

            if (active >= 2) { HandleTwoFinger(touches); return; }

            gestureActive = false;

            // Deliberately NOT gated on `active == 1`. A quick tap can report
            // wasPressedThisFrame on the same frame that isPressed has already gone false, so
            // counting held touches first silently swallowed short taps - the board simply never
            // appeared. HandleOneFinger already decides for itself using wasPressedThisFrame
            // (first drop) and isPressed (dragging), which is the same pattern
            // UnitDeploymentController uses and why deployment taps always worked.
            HandleOneFinger(touchscreen.primaryTouch);
        }

        private void HandleOneFinger(TouchControl touch)
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(touch.touchId.ReadValue()))
                return;

            bool pressed = touch.press.wasPressedThisFrame;
            bool held = touch.press.isPressed;
            if (!pressed && !held) return;

            Vector2 screenPos = touch.position.ReadValue();
            if (!TryRaycast(screenPos, out Vector3 point)) return;

            // First tap drops the board; subsequent dragging slides it. Both go through the same
            // path so the board always sits exactly where the finger is.
            if (!HasPlacedBoard)
            {
                if (!pressed) return;

                boardRoot.gameObject.SetActive(true);
                FaceAwayFromPlayer(point);
                HasPlacedBoard = true;
                OnBoardPlacedChanged?.Invoke(true);
            }

            boardRoot.position = point;
            ApplyLength(currentLength);
            PublishBoardPlane();
        }

        private void HandleTwoFinger(UnityEngine.InputSystem.Utilities.ReadOnlyArray<TouchControl> touches)
        {
            if (!HasPlacedBoard) return;

            if (!TryGetTwoTouches(touches, out Vector2 a, out Vector2 b)) return;

            float pinch = Vector2.Distance(a, b);
            Vector2 delta = b - a;
            float twist = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

            if (!gestureActive)
            {
                // Capture the baseline once, so scale and rotation are measured against the start
                // of the gesture rather than accumulating frame-to-frame drift.
                gestureActive = true;
                gestureStartPinchDistance = Mathf.Max(pinch, 1f);
                gestureStartLength = currentLength;
                gestureStartTwistAngle = twist;
                gestureStartYaw = boardRoot.eulerAngles.y;
                return;
            }

            float scaleFactor = pinch / gestureStartPinchDistance;
            ApplyLength(Mathf.Clamp(gestureStartLength * scaleFactor, MinBoardLengthWorld, MaxBoardLengthWorld));

            float twistDelta = Mathf.DeltaAngle(gestureStartTwistAngle, twist) * rotationSensitivity;
            boardRoot.rotation = Quaternion.Euler(0f, gestureStartYaw - twistDelta, 0f);

            PublishBoardPlane();
        }

        private static bool TryGetTwoTouches(UnityEngine.InputSystem.Utilities.ReadOnlyArray<TouchControl> touches, out Vector2 a, out Vector2 b)
        {
            a = default;
            b = default;
            int found = 0;

            for (int i = 0; i < touches.Count && found < 2; i++)
            {
                if (!touches[i].press.isPressed) continue;
                if (found == 0) a = touches[i].position.ReadValue();
                else b = touches[i].position.ReadValue();
                found++;
            }
            return found == 2;
        }

        /// <summary>
        /// Falls back through progressively weaker trackable types. PlaneWithinPolygon is the
        /// honest answer, but on the bland tables this project keeps failing on, an estimated plane
        /// or a feature point is far better than refusing to place the board at all.
        /// </summary>
        private bool TryRaycast(Vector2 screenPos, out Vector3 point)
        {
            const TrackableType types = TrackableType.PlaneWithinPolygon
                                        | TrackableType.PlaneEstimated
                                        | TrackableType.FeaturePoint;

            if (raycastManager.Raycast(screenPos, hits, types))
            {
                point = hits[0].pose.position;
                return true;
            }

            point = default;
            return false;
        }

        /// <summary>
        /// Orients the board so its far end (+z, the enemy's side) points away from the player.
        /// Authored levels assume z=0 is the player's edge, so getting this wrong would spawn every
        /// map backwards.
        /// </summary>
        private void FaceAwayFromPlayer(Vector3 boardPosition)
        {
            if (arCamera == null) return;

            Vector3 away = boardPosition - arCamera.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) return;

            boardRoot.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }

        /// <summary>
        /// Tells the preview what shape the chosen level's board is. Called by LevelMatchController
        /// once the level is known, so the outline the player fits to their table is the real
        /// footprint rather than a generic square.
        /// </summary>
        public void SetBoardAspect(float aspect)
        {
            boardAspect = Mathf.Clamp(aspect, 0.2f, 1f);
            BuildOutline();
        }

        /// <summary>
        /// Draws the board footprint as a closed loop in the board root's LOCAL space, so it moves,
        /// scales and rotates with the board for free. Without this the player is dragging an
        /// invisible object - they tap, nothing appears, and there is nothing to aim a pinch at.
        /// </summary>
        private void BuildOutline()
        {
            if (boardRoot == null) return;

            if (outlineMaterial == null)
            {
                Debug.LogWarning("BoardPlacementController: Outline Material is not assigned - the board footprint will be invisible while placing it.", this);
                return;
            }

            if (outline == null)
            {
                var go = new GameObject("BoardOutline");
                go.transform.SetParent(boardRoot, false);
                outline = go.AddComponent<LineRenderer>();
                outline.useWorldSpace = false; // follow the board root's transform
                outline.loop = true;
                outline.positionCount = 4;
                outline.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                outline.receiveShadows = false;

                outlineInstance = new Material(outlineMaterial) { color = outlineColor };
                outline.material = outlineInstance;
            }

            // Local space: board is 1.0 long on z and boardAspect wide on x, centred on the root -
            // matching LevelDefinition.ToBoardLocal so the preview and the built level agree.
            float hx = boardAspect * 0.5f;
            const float hz = 0.5f;
            outline.SetPosition(0, new Vector3(-hx, outlineLift, -hz));
            outline.SetPosition(1, new Vector3(hx, outlineLift, -hz));
            outline.SetPosition(2, new Vector3(hx, outlineLift, hz));
            outline.SetPosition(3, new Vector3(-hx, outlineLift, hz));

            UpdateOutlineWidth();
        }

        /// <summary>
        /// Line width is in world units but the renderer lives under a scaled root, so it has to be
        /// divided back out - otherwise the outline thickens as the player pinches the board bigger.
        /// </summary>
        private void UpdateOutlineWidth()
        {
            if (outline == null) return;
            // outlineWidth is a real-world thickness, so it converts; outlineLift does not, because
            // it is applied in board-local space and the root's scale already carries board length.
            float scale = Mathf.Max(boardRoot.localScale.x, 0.0001f);
            outline.widthMultiplier = WorldScale.Metres(outlineWidth) / scale;
        }

        private void OnDestroy()
        {
            if (outlineInstance != null) Destroy(outlineInstance);
        }

        private void ApplyLength(float length)
        {
            currentLength = length;
            boardRoot.localScale = new Vector3(length, length, length);
            UpdateOutlineWidth();
        }

        private void PublishBoardPlane()
        {
            // Length travels with the position: every gameplay distance downstream (unit speed,
            // sentry range, rally snap, deploy scatter) is scaled against it, and the player can
            // still be pinching the board bigger or smaller at this point.
            if (boardPlane != null) boardPlane.SetBoard(boardRoot.position, boardRoot.localScale.z);
        }

        /// <summary>Wire to the Confirm button. Locks the fit and hands off to the siege.</summary>
        public void ConfirmPlacement()
        {
            if (!HasPlacedBoard)
            {
                Debug.Log("BoardPlacementController: nothing placed yet - tap the table to drop the board first.");
                return;
            }

            PublishBoardPlane();
            enabled = false;
            OnPlacementConfirmed?.Invoke();
        }

        /// <summary>Wire to a "Reposition" button - lets the player re-fit without restarting.</summary>
        public void ResumePlacement()
        {
            enabled = true;
        }

        public float CurrentBoardLength => currentLength;
    }
}
