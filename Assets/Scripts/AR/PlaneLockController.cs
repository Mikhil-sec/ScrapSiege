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

        [Tooltip("Legacy scan/Fortify flow. Leave assigned only if using the scavenged-terrain path.")]
        [SerializeField] private FortifyInputController fortify;

        [Tooltip("The authored-level flow. When assigned, locking a plane starts board placement " +
                 "instead of Fortify - this is the current main path.")]
        [SerializeField] private ScrapSiege.Levels.BoardPlacementController boardPlacement;

        [Header("Lock rules")]
        // NOT scaled by WorldScale, deliberately. ARPlane.boundary is in plane-LOCAL space, so
        // PolygonArea returns genuine real-world m^2 no matter what uniform scale the XR Origin
        // carries - only the plane's rendered size changes. Multiplying this by WorldScale would
        // silently demand a ~70cm square table before the Lock button ever lit up.
        [Tooltip("Smallest polygon area (REAL m^2) accepted as a table. 0.02 is roughly a 14cm square - " +
                 "deliberately small, because ARCore grows a plane outward from a tiny seed and the " +
                 "player should be able to commit as soon as the board is plausibly covered.")]
        [SerializeField] private float minLockableArea = 0.02f;

        [Tooltip("How often (seconds) to re-evaluate whether a lockable plane exists.")]
        [SerializeField] private float candidatePollInterval = 0.25f;

        [Header("Diagnostics")]
        [Tooltip("Log why no plane is lockable yet. Pull with: adb logcat -d -s Unity:V")]
        [SerializeField] private bool logScanDiagnostics = true;
        [SerializeField] private float diagnosticIntervalSeconds = 2f;

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
        private float nextDiagnosticTime;

        private void Awake()
        {
            if (planeManager == null) Debug.LogError("PlaneLockController: Plane Manager is not assigned - scanning and locking will not work.", this);
            if (raycastManager == null) Debug.LogError("PlaneLockController: Raycast Manager is not assigned - the aimed-plane preference will fall back to largest-plane.", this);
            if (fortify == null && boardPlacement == null)
                Debug.LogError("PlaneLockController: neither Fortify nor Board Placement is assigned - locking a plane will lead nowhere.", this);

            // Remember whatever detection mode the scene was authored with so Rescan restores
            // exactly that, rather than hardcoding an assumption about horizontal-only.
            if (planeManager != null && planeManager.requestedDetectionMode != PlaneDetectionMode.None)
                scanDetectionMode = planeManager.requestedDetectionMode;

            // Neither the Fortify nor the placement phase may run until a plane is locked. Enforced
            // here rather than trusting the Inspector checkbox, matching how UnitDeploymentController
            // gates itself.
            if (fortify != null) fortify.enabled = false;
            if (boardPlacement != null) boardPlacement.enabled = false;
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

            if (!ready) LogScanDiagnostics();
        }

        /// <summary>
        /// Explains, in logcat, exactly why nothing is lockable yet. "Lock stays grey" has three
        /// completely different causes - ARCore has found nothing at all, it has found only
        /// vertical/downward surfaces, or it has a horizontal one that is still too small - and
        /// they are indistinguishable from the phone screen.
        /// </summary>
        private void LogScanDiagnostics()
        {
            if (!logScanDiagnostics || planeManager == null) return;
            if (Time.time < nextDiagnosticTime) return;
            nextDiagnosticTime = Time.time + diagnosticIntervalSeconds;

            int total = 0;
            var sb = new System.Text.StringBuilder("[PlaneLock] no lockable plane. detectionMode=");
            sb.Append(planeManager.requestedDetectionMode).Append(" planes=");

            foreach (var plane in planeManager.trackables)
            {
                total++;
                sb.Append("\n  id=").Append(plane.trackableId)
                  .Append(" align=").Append(plane.alignment)
                  .Append(" state=").Append(plane.trackingState)
                  .Append(" area=").Append(PolygonArea(plane).ToString("0.000"))
                  .Append("m2 subsumed=").Append(plane.subsumedBy != null)
                  .Append(" boundaryPts=").Append(plane.boundary.Length);
            }

            if (total == 0) sb.Append("0 - ARCore has not mapped ANY surface yet (lighting/texture/motion).");
            else sb.Insert(sb.ToString().IndexOf("planes=") + 7, total.ToString());

            sb.Append("\n  need: alignment=HorizontalUp, not subsumed, area >= ").Append(minLockableArea).Append("m2");
            Debug.Log(sb.ToString());
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

            // Board placement is the current main path; Fortify is only started when placement is
            // absent, so a scene with both wired can't run two conflicting input phases at once.
            if (boardPlacement != null)
            {
                boardPlacement.enabled = true;
            }
            else if (fortify != null)
            {
                fortify.SetLockedPlane(LockedPlane);
                fortify.enabled = true;
            }

            OnLockReadyChanged?.Invoke(false);
            OnPlaneLocked?.Invoke();
        }

        /// <summary>
        /// Hides the locked plane's own outline, keeping the plane itself (and its raycast target)
        /// alive. Called once the board is committed: up to that point the outline is what tells the
        /// player which surface they locked, but during the siege it draws a large white polygon
        /// straight across the battlefield and competes with the board for attention.
        /// </summary>
        public void HideLockedPlaneVisual()
        {
            if (LockedPlane == null) return;

            foreach (var renderer in LockedPlane.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
        }

        /// <summary>
        /// Stops plane detection without locking anything. Used by the joining player, who never
        /// locks a plane but still must not have ARCore refining surfaces underneath an already
        /// agreed board - the same drift problem locking solves for the host.
        /// </summary>
        public void FreezeDetection()
        {
            if (planeManager != null)
                planeManager.requestedDetectionMode = PlaneDetectionMode.None;

            logScanDiagnostics = false;
            OnLockReadyChanged?.Invoke(false);
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

            // The board was positioned against the plane being thrown away, so it has to go too.
            if (boardPlacement != null)
            {
                boardPlacement.enabled = false;
                if (boardPlacement.BoardRoot != null)
                    boardPlacement.BoardRoot.gameObject.SetActive(false);
            }

            // Undo HideLockedPlaneVisual - rescanning has to show outlines again or the player is
            // sweeping a table with no feedback at all.
            if (LockedPlane != null)
                foreach (var renderer in LockedPlane.GetComponentsInChildren<Renderer>(true))
                    renderer.enabled = true;

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
