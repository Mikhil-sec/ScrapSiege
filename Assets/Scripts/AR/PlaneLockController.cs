using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using ScrapSiege.Terrain;

namespace ScrapSiege.AR
{
    /// <summary>
    /// Owns the Scan phase that now runs *before* Fortify: ARCore keeps finding and growing
    /// planes while the player sweeps the phone over the table, and when the mapped surface
    /// looks right the player taps Lock to commit to exactly one plane.
    ///
    /// Locking does three things: freezes plane detection, hides every plane except the chosen
    /// one (the game is played on one table, and stray floor/counter planes both clutter the
    /// view and let corner taps land off-table), and enables FortifyInputController - which
    /// stays disabled until then so taps can't place terrain against a plane that's still
    /// growing under it.
    ///
    /// Rescan reverses all of that. It also clears any terrain already placed, because those
    /// objects were positioned against the plane that's being thrown away.
    /// </summary>
    public class PlaneLockController : MonoBehaviour
    {
        [SerializeField] private ARPlaneManager planeManager;
        [SerializeField] private ARRaycastManager raycastManager;
        [SerializeField] private FortifyInputController fortify;

        [Header("Lock rules")]
        [Tooltip("Smallest polygon area (m^2) accepted as a table. 0.06 is roughly a 25cm square.")]
        [SerializeField] private float minLockableArea = 0.06f;

        [Tooltip("How often (seconds) to re-evaluate whether a lockable plane exists.")]
        [SerializeField] private float candidatePollInterval = 0.25f;

        [Header("Events")]
        public UnityEvent OnScanStarted;
        public UnityEvent OnPlaneLocked;

        /// <summary>True once a plane large enough to lock is in view - drives the Lock button.</summary>
        public UnityEvent<bool> OnLockReadyChanged;

        /// <summary>Area (m^2) of the plane that would be locked right now, or 0 if none.</summary>
        public UnityEvent<float> OnMappedAreaChanged;

        public ARPlane LockedPlane { get; private set; }
        public bool IsLocked => LockedPlane != null;

        private readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();
        private PlaneDetectionMode scanDetectionMode = PlaneDetectionMode.Horizontal;
        private bool lastLockReady;
        private float lastReportedArea = -1f;
        private float nextPollTime;

        private void Awake()
        {
            if (planeManager == null) Debug.LogError("PlaneLockController: Plane Manager is not assigned - scanning and locking will not work.", this);
            if (raycastManager == null) Debug.LogError("PlaneLockController: Raycast Manager is not assigned - the aimed-plane preference will fall back to largest-plane.", this);
            if (fortify == null) Debug.LogError("PlaneLockController: Fortify is not assigned - locking a plane will not start the Fortify phase.", this);

            // Remember whatever detection mode the scene was authored with so Rescan restores
            // exactly that, rather than hardcoding an assumption about horizontal-only.
            if (planeManager != null && planeManager.requestedDetectionMode != PlaneDetectionMode.None)
                scanDetectionMode = planeManager.requestedDetectionMode;

            // Fortify must not run until a plane is locked. Enforced here rather than trusting
            // the Inspector checkbox, matching how UnitDeploymentController gates itself.
            if (fortify != null) fortify.enabled = false;
        }

        private void OnEnable()
        {
            if (planeManager != null)
                planeManager.trackablesChanged.AddListener(OnTrackablesChanged);

            BeginScan();
        }

        private void OnDisable()
        {
            if (planeManager != null)
                planeManager.trackablesChanged.RemoveListener(OnTrackablesChanged);
        }

        private void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARPlane> args)
        {
            if (!IsLocked) return;

            // Detection is off while locked, but ARCore can still surface a plane that was
            // already in flight. Anything new must stay hidden - one plane, no more.
            foreach (var plane in args.added)
                if (plane != LockedPlane) plane.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (IsLocked || Time.time < nextPollTime) return;
            nextPollTime = Time.time + candidatePollInterval;

            var candidate = FindCandidate();
            float area = candidate != null ? PolygonArea(candidate) : 0f;

            bool ready = candidate != null;
            if (ready != lastLockReady)
            {
                lastLockReady = ready;
                OnLockReadyChanged?.Invoke(ready);
            }

            if (!Mathf.Approximately(area, lastReportedArea))
            {
                lastReportedArea = area;
                OnMappedAreaChanged?.Invoke(area);
            }
        }

        /// <summary>Wire to the "Lock Table" button.</summary>
        public void LockPlane()
        {
            if (IsLocked) return;

            var candidate = FindCandidate();
            if (candidate == null)
            {
                Debug.Log("PlaneLockController: nothing lockable yet - keep sweeping the phone until a surface is mapped.");
                return;
            }

            LockedPlane = candidate;

            // Same reason FinishFortify() froze detection before: ARCore keeps refining and
            // extending boundaries as it tracks more of the room, which makes the locked table
            // visibly drift. Now that the player has explicitly committed, stop it here instead.
            if (planeManager != null)
                planeManager.requestedDetectionMode = PlaneDetectionMode.None;

            SetOtherPlanesVisible(false);
            SetOutlineLocked(LockedPlane, true);

            if (fortify != null)
            {
                fortify.SetLockedPlane(LockedPlane);
                fortify.enabled = true;
            }

            OnLockReadyChanged?.Invoke(false);
            OnPlaneLocked?.Invoke();
        }

        /// <summary>
        /// Wire to the "Rescan" button. Throws away the locked plane and any terrain placed on
        /// it, and puts ARCore back into detection so the player can map the table again.
        /// </summary>
        public void RescanPlane()
        {
            if (fortify != null)
            {
                fortify.ClearAllObjects();
                fortify.SetLockedPlane(null);
                fortify.enabled = false;
            }

            SetOutlineLocked(LockedPlane, false);
            LockedPlane = null;

            BeginScan();
        }

        private void BeginScan()
        {
            if (planeManager != null)
            {
                planeManager.requestedDetectionMode = scanDetectionMode;
                foreach (var plane in planeManager.trackables)
                    plane.gameObject.SetActive(true);
            }

            // Force both readouts to re-fire on the next poll even if the values are unchanged,
            // so the freshly-shown scan UI is never left blank.
            lastLockReady = false;
            lastReportedArea = -1f;
            nextPollTime = 0f;

            OnLockReadyChanged?.Invoke(false);
            OnScanStarted?.Invoke();
        }

        /// <summary>
        /// Prefers whatever the player is actually pointing at (screen centre), because aiming
        /// at the table is the natural way to say "that one". Falls back to the largest mapped
        /// surface when the centre of the screen isn't over a plane at the moment of the tap.
        /// </summary>
        private ARPlane FindCandidate()
        {
            if (planeManager == null) return null;

            if (raycastManager != null)
            {
                var screenCentre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                if (raycastManager.Raycast(screenCentre, hits, TrackableType.PlaneWithinPolygon))
                {
                    var aimed = planeManager.GetPlane(hits[0].trackableId);
                    if (IsUsable(aimed)) return aimed;
                }
            }

            ARPlane best = null;
            float bestArea = 0f;
            foreach (var plane in planeManager.trackables)
            {
                if (!IsUsable(plane)) continue;

                float area = PolygonArea(plane);
                if (area <= bestArea) continue;

                bestArea = area;
                best = plane;
            }
            return best;
        }

        private bool IsUsable(ARPlane plane)
        {
            // subsumedBy != null means ARCore merged this plane into a bigger one - locking the
            // absorbed child would give a surface that stops being updated underneath us.
            return plane != null
                   && plane.subsumedBy == null
                   && plane.alignment == PlaneAlignment.HorizontalUp
                   && PolygonArea(plane) >= minLockableArea;
        }

        /// <summary>
        /// Shoelace area of the plane's real boundary polygon. Preferred over plane.size (the
        /// bounding rectangle), which badly overstates an L-shaped or partially mapped table -
        /// this number is shown to the player, so it should mean what it says.
        /// </summary>
        private static float PolygonArea(ARPlane plane)
        {
            var boundary = plane.boundary;
            if (boundary.Length < 3) return 0f;

            float doubleArea = 0f;
            for (int i = 0; i < boundary.Length; i++)
            {
                Vector2 a = boundary[i];
                Vector2 b = boundary[(i + 1) % boundary.Length];
                doubleArea += a.x * b.y - b.x * a.y;
            }
            return Mathf.Abs(doubleArea) * 0.5f;
        }

        private void SetOtherPlanesVisible(bool visible)
        {
            if (planeManager == null) return;

            foreach (var plane in planeManager.trackables)
                if (plane != LockedPlane) plane.gameObject.SetActive(visible);
        }

        private static void SetOutlineLocked(ARPlane plane, bool locked)
        {
            if (plane == null) return;

            var outline = plane.GetComponent<PlaneOutlineVisualizer>();
            if (outline != null) outline.SetLocked(locked);
        }
    }
}
