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

        [Tooltip("Camera the deploy tap is cast from. Optional - falls back to Camera.main, which " +
                 "is the AR camera in the match scene.")]
        [SerializeField] private Camera deployCamera;

        [Tooltip("The legacy scan/Fortify flow. Leave assigned as the fallback path.")]
        [SerializeField] private SiegePhaseController siegePhase;

        [Tooltip("The authored-level flow. When assigned and a level is built, its enemy base wins " +
                 "over siegePhase's dummy base.")]
        [SerializeField] private ScrapSiege.Levels.LevelMatchController levelMatch;

        [Tooltip("The Rally order. Assigned so a tap that is placing a rally point cannot ALSO be " +
                 "read as a deploy - both components poll the same touch from their own Update.")]
        [SerializeField] private RallyController rallyController;

        [SerializeField] private GameObject unitPrefab;

        [Tooltip("Fallback cost, used only when no unit class is selected (the legacy scan/Fortify " +
                 "path). Every class carries its own cost.")]
        [SerializeField] private int unitCost = 1;

        [Header("Unit classes")]
        [Tooltip("The deployable roster. When assigned, the selected class decides cost, stats and " +
                 "silhouette; leave it null to keep the single-unit behaviour the scan flow uses.")]
        [SerializeField] private UnitRoster roster;

        [Header("Line of sight (plan.md Mechanic 2)")]
        [Tooltip("Refuse to deploy onto a point the player cannot actually see from where they are " +
                 "standing. This is the strongest single lever the game has for making the AR real: " +
                 "with it on, opening a route means physically moving until you can see the ground " +
                 "you want to put someone on, and tall terrain becomes a wall you must walk around " +
                 "rather than a texture you look over. Turn it off to fall back to deploy-anywhere.")]
        [SerializeField] private bool requireLineOfSight = true;

        [Tooltip("Which layers block a deploy sightline. Must match LineOfSightController's mask or " +
                 "the reticle and the rule will disagree with each other.")]
        [SerializeField] private LayerMask sightBlockerMask = 1 << ScrapSiege.Core.SiegeLayers.TerrainOccluder;

        [Tooltip("Ignore blockers this close to the camera, so the player's own held position " +
                 "clipping a wall does not black out the whole board. Real metres.")]
        [SerializeField] private float sightNearClip = 0.02f;

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

        private const float RejectedTapLogInterval = 2f;
        private float lastRejectedTapLogTime = -99f;

        private UnitClass selectedClass;

        /// <summary>
        /// Fires with a human-readable reason whenever a tap is refused, so the HUD can say what
        /// went wrong. "I tap and nothing happens" is the hardest symptom in this project to
        /// diagnose from a bug report, and now that line of sight can refuse a tap it would also be
        /// the most common thing a new player runs into.
        /// </summary>
        // Initialised inline rather than relying on Unity to construct them during deserialization.
        // Every listener here subscribes without a null check, so a null event is an immediate
        // NullReferenceException on scene load - cheap insurance for a field nobody wires by hand.
        public UnityEngine.Events.UnityEvent<string> OnDeployRejected = new UnityEngine.Events.UnityEvent<string>();

        /// <summary>Fires with the newly selected class so the HUD and RallyController can follow it.</summary>
        public UnityEngine.Events.UnityEvent<UnitClass> OnSelectedClassChanged = new UnityEngine.Events.UnityEvent<UnitClass>();

        /// <summary>What the next tap will deploy. Null on the legacy single-unit path.</summary>
        public UnitClass SelectedClass => selectedClass;

        /// <summary>Scrap the next deploy will cost.</summary>
        public int SelectedCost => selectedClass != null ? selectedClass.cost : unitCost;

        /// <summary>The roster this match is drawing from, so the HUD can build its bar from it.</summary>
        public UnitRoster Roster => roster;

        private void Awake()
        {
            // Must not process taps during Fortify - only SiegePhaseController.StartSiege()
            // turns this on. Enforced here rather than relying on the Inspector checkbox.
            enabled = false;

            // Only an error on the legacy scan/Fortify path - the authored-level path resolves taps
            // against the placed board and never touches the AR raycaster.
            if (raycastManager == null && levelMatch == null)
                Debug.LogError("UnitDeploymentController: Ray Cast Manager is not assigned and there is no Level Match to supply a board - taps can never resolve.", this);
            if (resourceEconomy == null) Debug.LogError("UnitDeploymentController: Resource Economy is not assigned.", this);
            if (siegePhase == null && levelMatch == null)
                Debug.LogError("UnitDeploymentController: neither Siege Phase nor Level Match is assigned - deployed units will have nothing to attack.", this);
            if (unitPrefab == null) Debug.LogError("UnitDeploymentController: Unit Prefab is not assigned.", this);
            else if (unitPrefab.GetComponent<SiegeUnit>() == null)
                Debug.LogError("UnitDeploymentController: Unit Prefab has no SiegeUnit component.", this);
            if (rallyController == null)
                Debug.LogError("UnitDeploymentController: Rally Controller is not assigned - a tap that places a rally point will ALSO deploy and charge for a unit.", this);

            // Even though OnEnable resolves this again at siege start, do it here too: the HUD and
            // the roster bar both read SelectedClass on their own first frame, and a null selection
            // shows the player a bar with nothing highlighted.
            ResolveDefaultClass();
        }

        /// <summary>
        /// Picks the opening class.
        ///
        /// <para><b>Not in Start.</b> <see cref="Awake"/> sets <c>enabled = false</c> so no tap is
        /// processed before Siege begins - and Unity never calls Start on a component that is
        /// disabled before its first frame. A Start-based default therefore silently never ran:
        /// verified in play mode, where the roster bar had built all five chips while
        /// <see cref="SelectedClass"/> was still null and every deploy would have cost the fallback
        /// price and spawned a class-less unit.</para>
        ///
        /// <para>Resolved again in <see cref="OnEnable"/> (i.e. when Siege starts) because
        /// <see cref="UnitRoster.DefaultClass"/> consults <see cref="ScrapSiege.Monetization.ProEntitlement"/>,
        /// which resolves asynchronously - by siege start it has had ample time, whereas at Awake it
        /// may not have.</para>
        /// </summary>
        private void ResolveDefaultClass()
        {
            if (roster == null) return;
            if (selectedClass != null && UnitRoster.IsUnlocked(selectedClass)) return;

            SelectClass(roster.DefaultClass());
        }

        private void OnEnable() => ResolveDefaultClass();

        /// <summary>
        /// Wire to a roster chip. Refuses locked classes rather than silently deploying something
        /// else - the paywall, not this, is what should answer a tap on a Pro class.
        /// </summary>
        public void SelectClass(UnitClass unitClass)
        {
            if (unitClass != null && !UnitRoster.IsUnlocked(unitClass)) return;
            if (selectedClass == unitClass) return;

            selectedClass = unitClass;
            OnSelectedClassChanged?.Invoke(selectedClass);
        }

        /// <summary>Wire to a "Deploy Direct" button - selects the fast/open route for the next tap.</summary>
        public void SelectDirectMode() => pendingMode = DeployMode.Direct;

        /// <summary>Wire to a "Deploy Covered" button - selects the slower/cover-hugging route for the next tap.</summary>
        public void SelectCoveredMode() => pendingMode = DeployMode.Covered;

        /// <summary>
        /// Where on the table a tap landed.
        ///
        /// The board is the primary target, intersected mathematically against its own transform
        /// rather than through an ARCore plane raycast. This is not an optimisation - it is a
        /// correctness fix. BoardPlacementController accepts `PlaneWithinPolygon | PlaneEstimated |
        /// FeaturePoint` hits, so a board can legitimately be placed on feature points alone, but
        /// this method used to accept `PlaneWithinPolygon` only. On a device where ARCore tracks
        /// fine yet never promotes anything to a plane (confirmed on the Tab S6 Lite: sixteen
        /// consecutive `[PlaneLock] ... planes=0` diagnostics across a whole session), that
        /// combination let the player place a board, start a match, and then have every single
        /// deploy tap silently discarded - a dead game with nothing logged and nothing on screen to
        /// explain it. Restarting could not help, because no restart makes a plane appear.
        ///
        /// The board slab deliberately carries no Collider (LevelBuilder builds it from a raw cube
        /// mesh precisely so nothing gets in the way of AR raycasts), hence a plane intersection
        /// rather than Physics.Raycast. Taps outside the board's own rectangle are rejected, which
        /// is stricter than the old behaviour and matches the design: units deploy onto the board,
        /// not onto any table surface that happens to be visible next to it.
        ///
        /// Falls back to the AR plane raycast for the legacy scan/Fortify path, which has no board.
        /// </summary>
        private bool TryResolveTapPoint(Vector2 screenPos, out Vector3 point)
        {
            Transform board = levelMatch != null ? levelMatch.BoardRoot : null;
            if (board != null && ResolveCamera() != null)
            {
                var boardPlane = new Plane(board.up, board.position);
                Ray ray = ResolveCamera().ScreenPointToRay(screenPos);
                if (boardPlane.Raycast(ray, out float distance))
                {
                    Vector3 hit = ray.GetPoint(distance);

                    // BoardRoot's local space is the unit square scaled to the board, so a point on
                    // the board has local x and z within +/-0.5 whatever the table size.
                    Vector3 local = board.InverseTransformPoint(hit);
                    if (Mathf.Abs(local.x) <= 0.5f && Mathf.Abs(local.z) <= 0.5f)
                    {
                        point = hit;
                        return true;
                    }
                }

                // A board exists and the tap missed it. Do not fall through to the AR raycast -
                // that would deploy onto bare table outside the battlefield.
                point = default;
                return false;
            }

            if (raycastManager != null && raycastManager.Raycast(screenPos, hits, TrackableType.PlaneWithinPolygon))
            {
                point = hits[0].pose.position;
                return true;
            }

            point = default;
            return false;
        }

        private Camera ResolveCamera()
        {
            if (deployCamera == null) deployCamera = Camera.main;
            return deployCamera;
        }

        /// <summary>
        /// Which deploy sound a class lands with. Every value currently synthesizes to the same
        /// recipe, so this changes nothing audible today - it exists so that dropping three files
        /// into <c>Assets/Audio/Resources/Sfx/</c> gives heavy, stealth and standard deploys their
        /// own weight without touching this code again.
        /// </summary>
        private static ScrapSiege.Audio.Sfx DeploySoundFor(UnitClass unitClass)
        {
            if (unitClass == null) return ScrapSiege.Audio.Sfx.Deploy;
            if (unitClass.invisibleToSentries) return ScrapSiege.Audio.Sfx.StealthDeploy;
            if (unitClass.IsStationary || unitClass.health >= 6) return ScrapSiege.Audio.Sfx.HeavyDeploy;

            return ScrapSiege.Audio.Sfx.Deploy;
        }

        /// <summary>
        /// Every rejection path here used to be a bare `return`, so "I tap and nothing happens" -
        /// the single hardest symptom to diagnose from a bug report - produced not one line of log.
        /// Throttled rather than per-tap because a frustrated player taps a lot.
        /// </summary>
        private void LogRejectedTap(string reason, string playerFacing = null)
        {
            // The player-facing message is raised every time - it is feedback, and throttling it
            // would mean the second tap of a frustrated pair got no answer. Only the log is capped.
            if (playerFacing != null) OnDeployRejected?.Invoke(playerFacing);

            if (Time.unscaledTime - lastRejectedTapLogTime < RejectedTapLogInterval) return;
            lastRejectedTapLogTime = Time.unscaledTime;
            Debug.Log($"[Deploy] tap ignored - {reason}. board={(levelMatch != null && levelMatch.BoardRoot != null ? "placed" : "none")}");
        }

        /// <summary>
        /// The AR-native half of deployment (plan.md Mechanic 2). A spot behind a wall from where
        /// the player is actually standing cannot be deployed onto - so opening a route means
        /// physically walking round the table, not selecting a different button.
        ///
        /// Uses the same static <see cref="ScrapSiege.Vision.LineOfSightController.HasClearLine"/>
        /// the reveal system does, deliberately: two implementations of "can I see this" would drift
        /// apart, and the failure mode of a mis-set occluder mask is silent (everything stays
        /// visible / everything stays deployable), which is exactly the class of bug this project
        /// keeps paying for.
        /// </summary>
        private bool HasSightlineTo(Vector3 point)
        {
            if (!requireLineOfSight) return true;

            Camera cam = ResolveCamera();
            if (cam == null) return true;

            // Aim slightly above the table so the ray does not graze the board slab itself.
            float lift = levelMatch != null && levelMatch.BoardLength > 0f
                ? levelMatch.BoardLength * 0.01f
                : ScrapSiege.Core.WorldScale.Metres(0.005f);

            return ScrapSiege.Vision.LineOfSightController.HasClearLine(
                cam.transform.position,
                point + Vector3.up * lift,
                sightBlockerMask,
                ScrapSiege.Core.WorldScale.Metres(sightNearClip));
        }

        private void Update()
        {
            var touch = Touchscreen.current?.primaryTouch;
            if (touch == null || !touch.press.wasPressedThisFrame) return;

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(touch.touchId.ReadValue()))
                return;

            // An armed Rally owns this tap. Both components poll the same touch from their own
            // Update, so before this the player got a rally AND a paid-for unit from one tap.
            if (rallyController != null && rallyController.ClaimsBoardTap) return;

            Vector2 screenPos = touch.position.ReadValue();
            if (!TryResolveTapPoint(screenPos, out Vector3 tapPoint))
            {
                LogRejectedTap("the tap did not land on the board", "Tap on the board.");
                return;
            }

            // The vantage mechanic lives here: leaned in, scatter is ~0 and the unit lands exactly
            // where tapped; pulled back for the overview, the drop spreads. Applied before the
            // NavMesh snap so a scattered point still resolves to somewhere walkable.
            Vector3 deployPoint = vantage != null
                ? vantage.ApplyScatter(tapPoint)
                : tapPoint;

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
            {
                LogRejectedTap($"no walkable NavMesh point within {snapDistance:0.###}m of the tap",
                               "Nothing can stand there.");
                return;
            }

            // Reinforcements arrive from your own lines. Checked against the SNAPPED point for the
            // same reason the sightline is - the point the unit will actually occupy is the one that
            // has to be legal, and both the scatter and the snap can move a tap several centimetres.
            if (levelMatch != null && !levelMatch.IsInDeployZone(navHit.position))
            {
                LogRejectedTap("the tap landed outside the player's deploy zone",
                               "Deploy inside your own lines.");
                return;
            }

            // Checked against the SNAPPED point, not the raw tap - the point a unit will actually
            // occupy is the one that has to be visible, and the snap can move a tap by several
            // centimetres of real table.
            if (!HasSightlineTo(navHit.position))
            {
                LogRejectedTap("no line of sight from the camera to the deploy point",
                               "No line of sight — move to see that ground.");
                return;
            }

            if (!ResolveTarget(out Vector3 targetPosition, out BaseHealth targetHealth))
            {
                LogRejectedTap("no enemy base to attack");
                return;
            }

            int cost = SelectedCost;
            if (!resourceEconomy.TrySpend(cost))
            {
                LogRejectedTap($"not enough scrap for {(selectedClass != null ? selectedClass.displayName : "a unit")} (cost {cost})",
                               $"Not enough scrap — {(selectedClass != null ? selectedClass.displayName : "unit")} costs {cost}.");
                return;
            }

            // After TrySpend, so the sound only fires on a deploy that actually happened - a tap
            // the player cannot afford should stay silent rather than sounding successful.
            ScrapSiege.Audio.GameAudio.Play(DeploySoundFor(selectedClass));

            var unit = Instantiate(unitPrefab, navHit.position, Quaternion.identity);
            var siegeUnit = unit.GetComponent<SiegeUnit>();

            // Explicit even though Team.Player is the default, because this is the one place a unit's
            // allegiance is decided and leaving it implicit invites the AI's spawn path to be written
            // the same way by copy-paste.
            siegeUnit.SetTeam(Team.Player);

            // Before ConfigureForBoard, which reads the class's engagement fraction and applies its
            // speed multiplier, and before SetTarget, which an emplacement answers differently.
            siegeUnit.ApplyClass(selectedClass);

            NavMeshAreas.ApplyCoverPreference(siegeUnit.Agent,
                                              preferCover: pendingMode == DeployMode.Covered,
                                              costMultiplier: siegeUnit.CoverCostVariance);

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
