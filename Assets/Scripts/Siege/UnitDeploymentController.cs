using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ScrapSiege.Siege
{
    /// <summary>
    /// Tap-to-deploy during Siege - mirrors FortifyInputController's tap-read pattern
    /// (including the UI-tap guard) so touch handling stays consistent across phases.
    /// Disabled until Siege begins; SiegePhaseController turns this on.
    /// </summary>
    public class UnitDeploymentController : MonoBehaviour
    {
        [SerializeField] private ARRaycastManager raycastManager;
        [SerializeField] private ResourceEconomy resourceEconomy;
        [SerializeField] private SiegePhaseController siegePhase;
        [SerializeField] private GameObject unitPrefab;
        [SerializeField] private int unitCost = 1;

        // How far to search for a valid walkable point near the tap - covers taps that land
        // just inside an obstacle's carved-out hole (e.g. tapping right next to a terrain object).
        [SerializeField] private float navMeshSnapDistance = 0.15f;

        private readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();

        private void Awake()
        {
            // Must not process taps during Fortify - only SiegePhaseController.StartSiege()
            // turns this on. Enforced here rather than relying on the Inspector checkbox.
            enabled = false;
        }

        private void Update()
        {
            var touch = Touchscreen.current?.primaryTouch;
            if (touch == null || !touch.press.wasPressedThisFrame) return;

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(touch.touchId.ReadValue()))
                return;

            Vector2 screenPos = touch.position.ReadValue();
            if (!raycastManager.Raycast(screenPos, hits, TrackableType.PlaneWithinPolygon)) return;

            // Snap to the nearest walkable point rather than spawning exactly on the tap -
            // taps close to an obstacle would otherwise land inside its carved-out hole, leaving
            // the agent with nothing valid nearby to attach to.
            if (!NavMesh.SamplePosition(hits[0].pose.position, out NavMeshHit navHit, navMeshSnapDistance, NavMesh.AllAreas))
                return;

            if (!resourceEconomy.TrySpend(unitCost)) return;

            var unit = Instantiate(unitPrefab, navHit.position, Quaternion.identity);
            unit.GetComponent<SiegeUnit>().SetDestination(siegePhase.DummyBase.position);
        }
    }
}
