using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using ScrapSiege.Terrain;

namespace ScrapSiege.Siege
{
    /// <summary>
    /// Tap-to-deploy during Siege - mirrors FortifyInputController's tap-read pattern
    /// (including the UI-tap guard) so touch handling stays consistent across phases.
    /// Disabled until Siege begins; SiegePhaseController turns this on.
    ///
    /// Route-variety trade-off: two deploy buttons instead of one. "Direct" units have the
    /// CoverLane NavMesh area excluded from their areaMask, so they always take the shortest
    /// open route. "Covered" units keep the default areaMask, so NavMeshAgent's own pathing will
    /// prefer detouring through CoverLane polygons (which TerrainObjectSpawner lays down next to
    /// RubbleCover/WallBarricade terrain) because that area's global cost is cheap
    /// (NavMeshAreas.CoverAreaCost) - no custom pathfinding needed, just area masks + area cost.
    /// GarrisonSentry is what makes the choice matter: it only damages units NOT currently
    /// standing in the CoverLane area.
    /// </summary>
    public class UnitDeploymentController : MonoBehaviour
    {
        private enum DeployMode
        {
            Direct,
            Covered
        }

        [SerializeField] private ARRaycastManager raycastManager;
        [SerializeField] private ResourceEconomy resourceEconomy;
        [SerializeField] private SiegePhaseController siegePhase;
        [SerializeField] private GameObject unitPrefab;
        [SerializeField] private int unitCost = 1;

        [Tooltip("Drives deploy precision from the phone's height above the board (plan.md Mechanic 1). " +
                 "Optional - if unassigned, every deploy lands exactly on the tap.")]
        [SerializeField] private ScrapSiege.Vantage.VantageController vantage;

        // How far to search for a valid walkable point near the tap - covers taps that land
        // just inside an obstacle's carved-out hole (e.g. tapping right next to a terrain object).
        [SerializeField] private float navMeshSnapDistance = 0.15f;

        private readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();
        private DeployMode pendingMode = DeployMode.Direct;

        private void Awake()
        {
            // Must not process taps during Fortify - only SiegePhaseController.StartSiege()
            // turns this on. Enforced here rather than relying on the Inspector checkbox.
            enabled = false;

            if (raycastManager == null) Debug.LogError("UnitDeploymentController: Ray Cast Manager is not assigned.", this);
            if (resourceEconomy == null) Debug.LogError("UnitDeploymentController: Resource Economy is not assigned.", this);
            if (siegePhase == null) Debug.LogError("UnitDeploymentController: Siege Phase is not assigned.", this);
            if (unitPrefab == null) Debug.LogError("UnitDeploymentController: Unit Prefab is not assigned.", this);
            else if (unitPrefab.GetComponent<SiegeUnit>() == null)
                Debug.LogError("UnitDeploymentController: Unit Prefab has no SiegeUnit component.", this);
        }

        /// <summary>Wire to a "Deploy Direct" button - selects the fast/open route for the next tap.</summary>
        public void SelectDirectMode() => pendingMode = DeployMode.Direct;

        /// <summary>Wire to a "Deploy Covered" button - selects the slower/cover-hugging route for the next tap.</summary>
        public void SelectCoveredMode() => pendingMode = DeployMode.Covered;

        private void Update()
        {
            var touch = Touchscreen.current?.primaryTouch;
            if (touch == null || !touch.press.wasPressedThisFrame) return;

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(touch.touchId.ReadValue()))
                return;

            Vector2 screenPos = touch.position.ReadValue();
            if (!raycastManager.Raycast(screenPos, hits, TrackableType.PlaneWithinPolygon)) return;

            // The vantage mechanic lives here: leaned in, scatter is ~0 and the unit lands exactly
            // where tapped; pulled back for the overview, the drop spreads. Applied before the
            // NavMesh snap so a scattered point still resolves to somewhere walkable.
            Vector3 deployPoint = vantage != null
                ? vantage.ApplyScatter(hits[0].pose.position)
                : hits[0].pose.position;

            // Snap to the nearest walkable point rather than spawning exactly on the tap -
            // taps close to an obstacle would otherwise land inside its carved-out hole, leaving
            // the agent with nothing valid nearby to attach to.
            if (!NavMesh.SamplePosition(deployPoint, out NavMeshHit navHit, navMeshSnapDistance, NavMesh.AllAreas))
                return;

            if (!resourceEconomy.TrySpend(unitCost)) return;

            var unit = Instantiate(unitPrefab, navHit.position, Quaternion.identity);
            var siegeUnit = unit.GetComponent<SiegeUnit>();

            if (pendingMode == DeployMode.Direct)
                siegeUnit.Agent.areaMask = NavMesh.AllAreas & ~NavMeshAreas.CoverAreaMask;

            siegeUnit.SetTarget(siegePhase.DummyBase.position, siegePhase.DummyBaseHealth);
        }
    }
}
