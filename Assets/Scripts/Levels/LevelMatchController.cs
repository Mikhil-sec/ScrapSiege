using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.Events;
using ScrapSiege.Siege;
using ScrapSiege.Terrain;
using ScrapSiege.Vantage;

namespace ScrapSiege.Levels
{
    /// <summary>
    /// Owns the authored-map match flow, replacing SiegePhaseController's Fortify handoff.
    ///
    /// Sequence: board confirmed -> build the level -> bake the NavMesh -> muster the garrison ->
    /// watch the win condition -> enable the siege systems. The order matters and is the same one
    /// SiegePhaseController established: terrain must exist before the bake, and the bake must
    /// exist before anything tries to path or snap to it.
    ///
    /// Kept separate from SiegePhaseController rather than folded into it, because the scan/Fortify
    /// path still works and is the fallback if authored placement proves unreliable on real tables.
    /// </summary>
    public class LevelMatchController : MonoBehaviour
    {
        [Header("Flow")]
        [SerializeField] private BoardPlacementController placement;
        [SerializeField] private LevelBuilder builder;
        [SerializeField] private LevelCatalog catalog;

        [Header("Systems enabled when the siege begins")]
        [SerializeField] private NavMeshSurface navMeshSurface;
        [SerializeField] private ResourceEconomy resourceEconomy;
        [SerializeField] private UnitDeploymentController deploymentController;
        [SerializeField] private MusterPhaseController musterPhase;
        [SerializeField] private SiegeOutcomeController outcomeController;
        [SerializeField] private RallyController rallyController;
        [SerializeField] private DeployReticle deployReticle;

        [Tooltip("Optional. Its locked-plane outline is hidden once the board is committed, so the " +
                 "AR plane polygon stops being drawn across the battlefield during the siege.")]
        [SerializeField] private ScrapSiege.AR.PlaneLockController planeLock;

        /// <summary>Fires once the siege is live - the HUD swaps to its Siege panel on this.</summary>
        public UnityEvent OnSiegeStarted;

        /// <summary>Fires with the level about to be played, so the HUD can show its name/briefing.</summary>
        public UnityEvent<string> OnLevelLoaded;

        public LevelDefinition ActiveLevel { get; private set; }

        /// <summary>
        /// The placed board's real length in metres, or 0 before placement. Anything tuned in
        /// metres - unit speed, arrival radii - has to be rescaled against this, because a level's
        /// layout is normalised but the table it lands on is whatever size the player chose.
        /// </summary>
        public float BoardLength => placement != null && placement.BoardRoot != null
            ? placement.BoardRoot.localScale.z
            : 0f;
        public Transform EnemyBase => builder != null ? builder.EnemyBase : null;
        public BaseHealth EnemyBaseHealth => builder != null ? builder.EnemyBaseHealth : null;

        private void Awake()
        {
            CheckRef(placement, nameof(placement));
            CheckRef(builder, nameof(builder));
            CheckRef(navMeshSurface, nameof(navMeshSurface));
            CheckRef(resourceEconomy, nameof(resourceEconomy));
            CheckRef(deploymentController, nameof(deploymentController));
            CheckRef(musterPhase, nameof(musterPhase));
            CheckRef(outcomeController, nameof(outcomeController));
        }

        private void CheckRef(Object reference, string fieldName)
        {
            if (reference == null)
                Debug.LogError($"LevelMatchController: '{fieldName}' is not assigned - the match will not start correctly.", this);
        }

        private void OnEnable()
        {
            if (placement != null) placement.OnPlacementConfirmed.AddListener(HandlePlacementConfirmed);
        }

        private void OnDisable()
        {
            if (placement != null) placement.OnPlacementConfirmed.RemoveListener(HandlePlacementConfirmed);
        }

        private void Start()
        {
            ActiveLevel = LevelCatalog.Selected;

            // Entering the match scene directly (Editor play, or a build that skipped the menu)
            // must still produce a playable level rather than an empty table.
            if (ActiveLevel == null && catalog != null)
            {
                ActiveLevel = catalog.FirstOrDefaultLevel();
                if (ActiveLevel != null)
                    Debug.Log($"LevelMatchController: no level selected, defaulting to '{ActiveLevel.displayName}'.");
            }

            if (ActiveLevel == null)
            {
                Debug.LogError("LevelMatchController: no level selected and the catalog is empty - there is nothing to build.", this);
                return;
            }

            OnLevelLoaded?.Invoke(ActiveLevel.displayName);

            // The placement preview has to match THIS level's footprint, not a generic square.
            if (placement != null) placement.SetBoardAspect(ActiveLevel.boardAspect);
        }

        private void HandlePlacementConfirmed()
        {
            if (ActiveLevel == null)
            {
                Debug.LogError("LevelMatchController: placement confirmed but no level is loaded.", this);
                return;
            }

            if (planeLock != null) planeLock.HideLockedPlaneVisual();

            builder.Build(ActiveLevel, placement.BoardRoot);

            // Terrain is final, so the walkable surface can be computed. Global area cost has to be
            // re-applied after every bake or the Covered route silently loses its discount.
            navMeshSurface.BuildNavMesh();
            NavMeshAreas.ApplyGlobalCost();

            if (musterPhase != null)
            {
                musterPhase.SetMaxGarrisonUnits(ActiveLevel.maxGarrisonUnits);

                // Sentries face the player's edge of the board (normalised z = 0), so their blind
                // side is the far side - reachable only by physically walking around the table.
                Vector3 threatOrigin = placement.BoardRoot.TransformPoint(
                    ActiveLevel.ToBoardLocal(new Vector2(0.5f, 0f)));
                musterPhase.SpawnGarrison(builder.SpawnedTerrain, threatOrigin);
            }

            if (builder.EnemyBaseHealth != null)
                outcomeController.WatchBase(builder.EnemyBaseHealth);
            else
                Debug.LogError("LevelMatchController: level built with no enemy base health - the win condition can never fire.", this);

            resourceEconomy.enabled = true;
            deploymentController.enabled = true;
            if (rallyController != null) rallyController.enabled = true;
            if (deployReticle != null) deployReticle.enabled = true;

            OnSiegeStarted?.Invoke();
        }
    }
}
