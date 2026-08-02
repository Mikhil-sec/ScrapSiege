using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
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

        [Header("Events - wire these to Fortify UI")]
        public UnityEvent OnAwaitingFirstCorner;
        public UnityEvent OnAwaitingSecondCorner;
        public UnityEvent OnAwaitingHeightPick;
        public UnityEvent<int> OnObjectCount;

        private State state = State.WaitingFirstCorner;
        private TerrainObjectSpawner spawner;
        private readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();
        private readonly List<TerrainObjectData> scannedObjects = new List<TerrainObjectData>();

        private Vector3 pendingCornerA;
        private GameObject cornerAMarker;

        private void Awake()
        {
            spawner = GetComponent<TerrainObjectSpawner>();
        }

        private void OnEnable()
        {
            OnAwaitingFirstCorner?.Invoke();
        }

        private void Update()
        {
            if (state == State.WaitingHeightPick) return;

            var touch = Touchscreen.current?.primaryTouch;
            if (touch == null || !touch.press.wasPressedThisFrame) return;

            Vector2 screenPos = touch.position.ReadValue();
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

                var data = new TerrainObjectData
                {
                    CornerA = pendingCornerA,
                    CornerB = worldPos
                };
                pendingObject = data;

                state = State.WaitingHeightPick;
                OnAwaitingHeightPick?.Invoke();
            }
        }

        private TerrainObjectData pendingObject;

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
            scannedObjects.Add(pendingObject);
            spawner.Spawn(pendingObject);
            pendingObject = null;

            OnObjectCount?.Invoke(scannedObjects.Count);

            state = State.WaitingFirstCorner;
            OnAwaitingFirstCorner?.Invoke();
        }

        /// <summary>Wire to a "Done Fortifying" button to end the phase.</summary>
        public void FinishFortify()
        {
            TerrainClassifier.ApplyWatchtowerOverride(scannedObjects);
            enabled = false;
        }
    }
}
