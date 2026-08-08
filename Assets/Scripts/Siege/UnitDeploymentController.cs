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
    /// Route-variety trade-off: two deploy buttons instead of one. Both modes can walk anywhere -
    /// they differ only in how much they VALUE cover, applied as a per-agent NavMesh area cost
    /// (NavMeshAreas.ApplyCoverPreference). "Direct" prices cover the same as open ground, so it
    /// takes the geometrically shortest line and passes through a cover lane only when that really
    /// is the shortest way - which is what lets a well-placed Direct drop thread the corridor.
    /// "Covered" prices cover far cheaper, so NavMeshAgent's own pathing detours to hug the
    /// CoverLane polygons TerrainObjectSpawner lays down next to RubbleCover/WallBarricade terrain -
    /// no custom pathfinding needed, just area costs. GarrisonSentry is what makes the choice
    /// matter: it only damages units NOT currently standing in the CoverLane area.
    ///
    /// Direct used to have the area excluded from its areaMask outright. That both contradicted the
    /// design (a Direct unit could never use the corridor at all, however well aimed) and broke the
    /// map: on a narrow board the cover polygons were the only connection between the two halves,
    /// so Direct units had no complete path to the enemy base and simply stopped partway.
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

        [Tooltip("The legacy scan/Fortify flow. Leave assigned as the fallback path.")]
        [SerializeField] private SiegePhaseController siegePhase;

        [Tooltip("The authored-level flow. When assigned and a level is built, its enemy base wins " +
                 "over siegePhase's dummy base.")]
        [SerializeField] private ScrapSiege.Levels.LevelMatchController levelMatch;

        [SerializeField] private GameObject unitPrefab;
        [SerializeField] private int unitCost = 1;

        [Tooltip("Drives deploy precision from the phone's height above the board (plan.md Mechanic 1). " +
                 "Optional - if unassigned, every deploy lands exactly on the tap.")]
        [SerializeField] private ScrapSiege.Vantage.VantageController vantage;

        // How far to search for a valid walkable point near the tap - covers taps that land
        // just inside an obstacle's carved-out hole (e.g. tapping right next to a terrain object).
        // Expressed as a fraction of board length: the old absolute 0.15m let a tap resolve a
        // quarter of a 0.60m board away, which quietly undermined the vantage precision mechanic.
        [SerializeField] private float navMeshSnapFraction = 0.04f;

        [Tooltip("Used only when no board length is available (the legacy scan/Fortify path).")]
        [SerializeField] private float navMeshSnapFallback = 0.05f;

        private readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();
        private DeployMode pendingMode = DeployMode.Direct;

        private void Awake()
        {
            // Must not process taps during Fortify - only SiegePhaseController.StartSiege()
            // turns this on. Enforced here rather than relying on the Inspector checkbox.
            enabled = false;

            if (raycastManager == null) Debug.LogError("UnitDeploymentController: Ray Cast Manager is not assigned.", this);
            if (resourceEconomy == null) Debug.LogError("UnitDeploymentController: Resource Economy is not assigned.", this);
            if (siegePhase == null && levelMatch == null)
                Debug.LogError("UnitDeploymentController: neither Siege Phase nor Level Match is assigned - deployed units will have nothing to attack.", this);
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
            //
            // Every area is walkable for both modes now, so one mask serves the sample and the
            // agent alike. Neither can be spawned onto ground it is not allowed to stand on.
            float boardLength = levelMatch != null ? levelMatch.BoardLength : 0f;
            float snapDistance = boardLength > 0f
                ? navMeshSnapFraction * boardLength
                : ScrapSiege.Core.WorldScale.Metres(navMeshSnapFallback);

            if (!NavMesh.SamplePosition(deployPoint, out NavMeshHit navHit, snapDistance, NavMesh.AllAreas))
                return;

            if (!ResolveTarget(out Vector3 targetPosition, out BaseHealth targetHealth)) return;

            if (!resourceEconomy.TrySpend(unitCost)) return;

            var unit = Instantiate(unitPrefab, navHit.position, Quaternion.identity);
            var siegeUnit = unit.GetComponent<SiegeUnit>();

            // Explicit even though Team.Player is the default, because this is the one place a unit's
            // allegiance is decided and leaving it implicit invites the AI's spawn path to be written
            // the same way by copy-paste.
            siegeUnit.SetTeam(Team.Player);

            NavMeshAreas.ApplyCoverPreference(siegeUnit.Agent, preferCover: pendingMode == DeployMode.Covered);

            // Before SetTarget, which multiplies the per-unit speed variance onto the base speed.
            if (boardLength > 0f) siegeUnit.ConfigureForBoard(boardLength);

            siegeUnit.SetTarget(targetPosition, targetHealth);
        }

        /// <summary>
        /// Prefers the authored level's enemy base, falling back to the scan flow's dummy base.
        /// Resolved per tap rather than cached, because the base only exists once the board has
        /// been placed and the level built - which happens after this component is first enabled.
        /// Returns false (and spends nothing) when there is no valid target, so a mis-wired scene
        /// can't silently burn the player's resources on units that will never attack anything.
        /// </summary>
        private bool ResolveTarget(out Vector3 position, out BaseHealth health)
        {
            if (levelMatch != null && levelMatch.EnemyBase != null)
            {
                position = levelMatch.EnemyBase.position;
                health = levelMatch.EnemyBaseHealth;
                return true;
            }

            if (siegePhase != null && siegePhase.DummyBase != null)
            {
                position = siegePhase.DummyBase.position;
                health = siegePhase.DummyBaseHealth;
                return true;
            }

            position = default;
            health = null;
            Debug.LogError("UnitDeploymentController: no enemy base exists yet - ignoring the deploy tap.", this);
            return false;
        }
    }
}
