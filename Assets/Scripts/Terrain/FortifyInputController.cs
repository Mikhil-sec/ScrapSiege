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

        [Header("Height-pick UI - shown only while picking a height for the current object")]
        [SerializeField] private GameObject[] heightPickButtons;

        [Header("Events - wire these to Fortify UI")]
        public UnityEvent OnAwaitingFirstCorner;
        public UnityEvent OnAwaitingSecondCorner;
        public UnityEvent OnAwaitingHeightPick;
        public UnityEvent<int> OnObjectCount;

        private State state = State.WaitingFirstCorner;
        private bool deleteMode;
        private TerrainObjectSpawner spawner;
        private readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();
        private readonly List<TerrainObjectData> scannedObjects = new List<TerrainObjectData>();

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

            if (!raycastManager.Raycast(screenPos, hits, TrackableType.PlaneWithinPolygon)) return;

            Vector3 worldPos = hits[0].pose.position;

            if (state == State.WaitingFirstCorner)
            {
                pendingCornerA = worldPos;
                cornerAMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                cornerAMarker.name = "FortifyCornerMarker";
                cornerAMarker.transform.position = worldPos;
                cornerAMarker.transform.localScale = Vector3.one * 0.02f;

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

            OnObjectCount?.Invoke(scannedObjects.Count);
        }

        /// <summary>Wire to a "Delete Object" toggle button - while active, tapping a placed object removes it.</summary>
        public void ToggleDeleteMode() => SetDeleteMode(!deleteMode);

        public void SetDeleteMode(bool enable)
        {
            deleteMode = enable;
            if (!deleteMode) return;

            // Entering delete mode mid-placement abandons whatever corner/height pick was in progress.
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
            TerrainClassifier.ApplyWatchtowerOverride(scannedObjects);
            enabled = false;
        }
    }
}
