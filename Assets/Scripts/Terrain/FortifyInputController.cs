using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ScrapSiege.Terrain
{
    /// <summary>
    /// Tier B terrain tagging (plan.md Mechanic 1): player taps two opposite corners of a
    /// real object on the live camera view, then picks a height category. Works on any
    /// AR-capable phone via plane hit-testing - no depth sensor required.
    /// </summary>
    [RequireComponent(typeof(TerrainObjectSpawner))]
    public class FortifyInputController : MonoBehaviour
    {
        private enum State
        {
            WaitingFirstCorner,
            WaitingSecondCorner,
            WaitingHeightPick
        }

        [SerializeField] private ARRaycastManager raycastManager;
        [SerializeField] private Camera arCamera;
        [SerializeField] private ARPlaneManager planeManager;

        [Header("Height-pick UI - shown only while picking a height for the current object")]
        [SerializeField] private GameObject[] heightPickButtons;

        [Header("Events - wire these to Fortify UI")]
        public UnityEvent OnAwaitingFirstCorner;
        public UnityEvent OnAwaitingSecondCorner;
        public UnityEvent OnAwaitingHeightPick;
        public UnityEvent<int> OnObjectCount;
        public UnityEvent<bool> OnDeleteModeChanged;

        private State state = State.WaitingFirstCorner;
        private bool deleteMode;
        private TerrainObjectSpawner spawner;
        private readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();
        private readonly List<TerrainObjectData> scannedObjects = new List<TerrainObjectData>();

        // Set by PlaneLockController once the player commits to a table. Corner taps are
        // restricted to this plane so a stray floor plane behind the table can't swallow a tap.
        private TrackableId lockedPlaneId = TrackableId.invalidId;

        /// <summary>Final scanned terrain, valid after FinishFortify() - used by MusterPhaseController.</summary>
        public IReadOnlyList<TerrainObjectData> ScannedObjects => scannedObjects;

        private Vector3 pendingCornerA;
        private GameObject cornerAMarker;
        private TerrainObjectData pendingObject;

        private void Awake()
        {
            spawner = GetComponent<TerrainObjectSpawner>();
            if (arCamera == null) arCamera = Camera.main;
            SetHeightPickButtonsVisible(false);
        }

        private void OnEnable()
        {
            OnAwaitingFirstCorner?.Invoke();
        }

        private void Update()
        {
            var touch = Touchscreen.current?.primaryTouch;
            if (touch == null || !touch.press.wasPressedThisFrame) return;

            // A tap that lands on a UI button (Done/Undo/Delete/height-pick) must not also be
            // interpreted as a world-space corner/delete tap on the table underneath it.
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(touch.touchId.ReadValue()))
                return;

            Vector2 screenPos = touch.position.ReadValue();

            if (deleteMode)
            {
                TryDeleteAtScreenPoint(screenPos);
                return;
            }

            if (state == State.WaitingHeightPick) return;

            if (!TryRaycastLockedPlane(screenPos, out Vector3 worldPos)) return;

            if (state == State.WaitingFirstCorner)
            {
                pendingCornerA = worldPos;
                cornerAMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                cornerAMarker.name = "FortifyCornerMarker";
                cornerAMarker.transform.position = worldPos;
                cornerAMarker.transform.localScale = Vector3.one * ScrapSiege.Core.WorldScale.Metres(0.02f);

                state = State.WaitingSecondCorner;
                OnAwaitingSecondCorner?.Invoke();
            }
            else if (state == State.WaitingSecondCorner)
            {
                if (cornerAMarker != null) Destroy(cornerAMarker);
                cornerAMarker = null;

                pendingObject = new TerrainObjectData
                {
                    CornerA = pendingCornerA,
                    CornerB = worldPos
                };

                state = State.WaitingHeightPick;
                SetHeightPickButtonsVisible(true);
                OnAwaitingHeightPick?.Invoke();
            }
        }

        /// <summary>
        /// Raycast that honours the locked board. AR raycast hits come back sorted nearest-first,
        /// so this walks them in order and takes the first one that belongs to the locked plane -
        /// a tap that only lands on some other surface is ignored entirely rather than silently
        /// placing terrain on the floor behind the table.
        /// </summary>
        private bool TryRaycastLockedPlane(Vector2 screenPos, out Vector3 worldPos)
        {
            worldPos = default;
            if (!raycastManager.Raycast(screenPos, hits, TrackableType.PlaneWithinPolygon)) return false;

            if (lockedPlaneId == TrackableId.invalidId)
            {
                worldPos = hits[0].pose.position;
                return true;
            }

            for (int i = 0; i < hits.Count; i++)
            {
                if (hits[i].trackableId != lockedPlaneId) continue;

                worldPos = hits[i].pose.position;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Called by PlaneLockController on lock (and with null on rescan). Restricts corner and
        /// delete taps to the one plane the player committed to.
        /// </summary>
        public void SetLockedPlane(ARPlane plane)
        {
            lockedPlaneId = plane != null ? plane.trackableId : TrackableId.invalidId;
        }

        /// <summary>Wire to a "Short" height-pick button.</summary>
        public void PickShort() => FinishHeightPick(HeightCategory.Short);

        /// <summary>Wire to a "Medium" height-pick button.</summary>
        public void PickMedium() => FinishHeightPick(HeightCategory.Medium);

        /// <summary>Wire to a "Tall" height-pick button.</summary>
        public void PickTall() => FinishHeightPick(HeightCategory.Tall);

        private void FinishHeightPick(HeightCategory category)
        {
            if (state != State.WaitingHeightPick || pendingObject == null) return;

            pendingObject.Height = category;
            pendingObject.Archetype = TerrainClassifier.Classify(pendingObject);
            pendingObject.Visual = spawner.Spawn(pendingObject);
            scannedObjects.Add(pendingObject);
            pendingObject = null;

            OnObjectCount?.Invoke(scannedObjects.Count);

            SetHeightPickButtonsVisible(false);
            state = State.WaitingFirstCorner;
            OnAwaitingFirstCorner?.Invoke();
        }

        /// <summary>Wire to an "Undo Last" button - removes the most recently placed object.</summary>
        public void UndoLastObject()
        {
            if (scannedObjects.Count == 0) return;

            var last = scannedObjects[scannedObjects.Count - 1];
            scannedObjects.RemoveAt(scannedObjects.Count - 1);
            if (last.Visual != null) Destroy(last.Visual);
            if (last.CoverVolume != null) Destroy(last.CoverVolume);

            OnObjectCount?.Invoke(scannedObjects.Count);
        }

        /// <summary>
        /// Removes every placed object at once. Used when PlaneLockController rescans - the
        /// terrain was positioned against the plane that's being discarded, so keeping it would
        /// leave objects floating relative to whatever plane gets locked next.
        /// </summary>
        public void ClearAllObjects()
        {
            foreach (var obj in scannedObjects)
            {
                if (obj.Visual != null) Destroy(obj.Visual);
                if (obj.CoverVolume != null) Destroy(obj.CoverVolume);
            }
            scannedObjects.Clear();

            CancelPendingPlacement();
            SetDeleteMode(false);

            OnObjectCount?.Invoke(0);
            OnAwaitingFirstCorner?.Invoke();
        }

        /// <summary>Wire to a "Delete Object" toggle button - while active, tapping a placed object removes it.</summary>
        public void ToggleDeleteMode() => SetDeleteMode(!deleteMode);

        public void SetDeleteMode(bool enable)
        {
            deleteMode = enable;
            OnDeleteModeChanged?.Invoke(deleteMode);

            if (!deleteMode) return;

            // Entering delete mode mid-placement abandons whatever corner/height pick was in progress.
            CancelPendingPlacement();
        }

        private void CancelPendingPlacement()
        {
            if (cornerAMarker != null)
            {
                Destroy(cornerAMarker);
                cornerAMarker = null;
            }
            pendingObject = null;
            SetHeightPickButtonsVisible(false);
            state = State.WaitingFirstCorner;
        }

        private void TryDeleteAtScreenPoint(Vector2 screenPos)
        {
            if (arCamera == null) return;
            if (!Physics.Raycast(arCamera.ScreenPointToRay(screenPos), out RaycastHit hit)) return;

            for (int i = 0; i < scannedObjects.Count; i++)
            {
                if (scannedObjects[i].Visual != hit.collider.gameObject) continue;

                Destroy(scannedObjects[i].Visual);
                if (scannedObjects[i].CoverVolume != null) Destroy(scannedObjects[i].CoverVolume);
                scannedObjects.RemoveAt(i);
                OnObjectCount?.Invoke(scannedObjects.Count);
                return;
            }
        }

        private void SetHeightPickButtonsVisible(bool visible)
        {
            if (heightPickButtons == null) return;
            foreach (var button in heightPickButtons)
                if (button != null) button.SetActive(visible);
        }

        /// <summary>Wire to a "Done Fortifying" button to end the phase.</summary>
        public void FinishFortify()
        {
            // A half-placed object (one corner tapped, or a height pick still open) would
            // otherwise leave its marker sphere floating over the board all through Siege.
            CancelPendingPlacement();

            TerrainClassifier.ApplyWatchtowerOverride(scannedObjects);
            enabled = false;

            // PlaneLockController already froze detection when the board was locked; this stays
            // as a safety net so Fortify can never hand off to Siege with ARCore still free to
            // refine and extend plane boundaries under the baked NavMesh.
            if (planeManager != null)
                planeManager.requestedDetectionMode = PlaneDetectionMode.None;
        }
    }
}
