using System.Collections.Generic;
using UnityEngine;
using ScrapSiege.Siege;
using ScrapSiege.Terrain;

namespace ScrapSiege.Levels
{
    /// <summary>
    /// Turns a <see cref="LevelDefinition"/> into real objects on a placed board.
    ///
    /// Everything is instantiated under a board root transform, so the board's position, yaw and
    /// scale are the single thing that maps normalised authoring space onto the player's real
    /// table. Move or resize the root and the whole battlefield follows, correctly.
    ///
    /// This deliberately reuses <see cref="TerrainObjectSpawner"/> rather than spawning its own
    /// visuals: NavMesh obstacle carving, CoverLane tagging, the Pro colour palette and the
    /// line-of-sight occluder layer all already live there, and duplicating any of it would let
    /// authored maps silently diverge from scanned ones.
    /// </summary>
    public class LevelBuilder : MonoBehaviour
    {
        [SerializeField] private TerrainObjectSpawner terrainSpawner;
        [SerializeField] private GameObject enemyBasePrefab;
        [SerializeField] private GameObject playerBasePrefab;

        [Tooltip("Flat quad used as the walkable surface the NavMesh bakes from.")]
        [SerializeField] private GameObject groundQuadPrefab;

        [Header("Board surface")]
        [Tooltip("Any opaque material using the active render pipeline - the board and its end zones are instanced from it.")]
        [SerializeField] private Material boardMaterial;

        [SerializeField] private Color boardColor = new Color(0.16f, 0.17f, 0.19f);
        [SerializeField] private Color playerZoneColor = new Color(0.20f, 0.45f, 0.85f);
        [SerializeField] private Color enemyZoneColor = new Color(0.80f, 0.22f, 0.20f);

        [Tooltip("Board thickness as a fraction of board length. Gives the slab a visible edge so it " +
                 "reads as a real board rather than a floating outline.")]
        [SerializeField] private float boardThickness = 0.018f;

        [Tooltip("Depth of each coloured end zone as a fraction of board length.")]
        [SerializeField] private float endZoneDepth = 0.12f;

        [Tooltip("How far forward of the player's own edge a unit may be deployed, as a fraction of " +
                 "board length. This is the ONE number the rule and the drawn zone both come from - " +
                 "UnitDeploymentController reads it back through LevelMatchController, so the band " +
                 "the player can see is exactly the ground the game will accept a tap on.")]
        [Range(0.05f, 0.9f)]
        [SerializeField] private float deployZoneDepth = 0.30f;

        [Tooltip("The bright line marking the forward limit of the deploy zone, as a fraction of " +
                 "board length. Purely visual.")]
        [SerializeField] private float deployLimitLineWidth = 0.006f;

        [SerializeField] private Color deployZoneColor = new Color(0.16f, 0.30f, 0.52f);
        [SerializeField] private Color deployLimitColor = new Color(0.42f, 0.72f, 1f);

        [Tooltip("Base footprint as a fraction of board length. The prefab's own scale is ignored so " +
                 "the objective stays proportionate on any board size.")]
        [SerializeField] private float baseFootprint = 0.13f;

        private readonly List<TerrainObjectData> spawned = new List<TerrainObjectData>();
        private readonly List<GameObject> ownedObjects = new List<GameObject>();

        /// <summary>Terrain built for the current level - fed to MusterPhaseController for garrison placement.</summary>
        public IReadOnlyList<TerrainObjectData> SpawnedTerrain => spawned;

        public Transform EnemyBase { get; private set; }
        public BaseHealth EnemyBaseHealth { get; private set; }
        public Transform PlayerBase { get; private set; }
        public BaseHealth PlayerBaseHealth { get; private set; }

        /// <summary>
        /// How far forward of the player's own edge (board-local z = -0.5) a deploy tap is allowed,
        /// as a fraction of board length. Read by <see cref="ScrapSiege.Siege.UnitDeploymentController"/>
        /// and <see cref="ScrapSiege.Vantage.DeployReticle"/> so the drawn zone and the enforced rule
        /// can never drift apart.
        /// </summary>
        public float DeployZoneDepth => Mathf.Clamp(deployZoneDepth, 0.02f, 1f);

        /// <summary>
        /// True when <paramref name="boardLocal"/> (a point already in the board root's local space)
        /// is inside the player's deploy zone.
        ///
        /// <para>The player's own edge is local -z by construction: <see cref="BuildBoardSurface"/>
        /// draws PlayerEndZone there and <see cref="LevelDefinition.ToBoardLocal"/> maps the
        /// normalised y of <c>playerBasePosition</c> (0.08 on every shipped level) onto it. Stated
        /// here once rather than re-derived per caller.</para>
        /// </summary>
        public bool IsInDeployZone(Vector3 boardLocal)
        {
            return boardLocal.z <= -0.5f + DeployZoneDepth
                   && boardLocal.z >= -0.5f
                   && Mathf.Abs(boardLocal.x) <= 0.5f;
        }

        private void Awake()
        {
            if (terrainSpawner == null) Debug.LogError("LevelBuilder: Terrain Spawner is not assigned - no terrain can be built.", this);
            if (enemyBasePrefab == null) Debug.LogError("LevelBuilder: Enemy Base Prefab is not assigned - the level will have no objective.", this);
            if (groundQuadPrefab == null) Debug.LogError("LevelBuilder: Ground Quad Prefab is not assigned - there will be nothing for the NavMesh to bake.", this);
        }

        /// <summary>
        /// Builds <paramref name="level"/> under <paramref name="boardRoot"/>. The root's scale is
        /// taken as the board's real length in metres, so a 0.5-scaled root is a 50cm board.
        /// Safe to call repeatedly - the previous build is torn down first.
        /// </summary>
        public void Build(LevelDefinition level, Transform boardRoot)
        {
            if (level == null) { Debug.LogError("LevelBuilder.Build: level is null.", this); return; }
            if (boardRoot == null) { Debug.LogError("LevelBuilder.Build: boardRoot is null.", this); return; }

            Clear();

            float boardLength = boardRoot.localScale.z;
            if (boardLength <= 0.01f)
            {
                Debug.LogError($"LevelBuilder.Build: board root scale {boardRoot.localScale} is degenerate - the level would be built at zero size.", this);
                return;
            }

            // Before any piece is spawned: cover lanes are sized from this, and a piece placed
            // without it would lay down a lane in absolute metres on a board measured in fractions.
            terrainSpawner.SetBoardLength(boardLength);

            BuildGround(level, boardRoot, boardLength);
            BuildBoardSurface(level, boardRoot);

            foreach (var placement in level.terrain)
                BuildTerrainPiece(level, placement, boardRoot, boardLength);

            EnemyBase = BuildBase(enemyBasePrefab, level, level.enemyBasePosition, boardRoot, boardLength, "EnemyBase", level.enemyBaseHealth, out var enemyHealth);
            EnemyBaseHealth = enemyHealth;

            if (playerBasePrefab != null)
            {
                PlayerBase = BuildBase(playerBasePrefab, level, level.playerBasePosition, boardRoot, boardLength, "PlayerBase", level.playerBaseHealth, out var playerHealth);
                PlayerBaseHealth = playerHealth;
            }

            Debug.Log($"LevelBuilder: built '{level.displayName}' - {spawned.Count} terrain pieces, board {boardLength:0.00}m x {boardLength * level.boardAspect:0.00}m.");
        }

        /// <summary>Destroys everything from the previous build. Called by Build and on level exit.</summary>
        public void Clear()
        {
            foreach (var data in spawned)
            {
                if (data.Visual != null) Destroy(data.Visual);
                if (data.CoverVolume != null) Destroy(data.CoverVolume);
            }
            spawned.Clear();

            foreach (var go in ownedObjects)
                if (go != null) Destroy(go);
            ownedObjects.Clear();

            EnemyBase = null;
            EnemyBaseHealth = null;
            PlayerBase = null;
            PlayerBaseHealth = null;
        }

        private void BuildGround(LevelDefinition level, Transform boardRoot, float boardLength)
        {
            var ground = Instantiate(groundQuadPrefab, boardRoot.position, boardRoot.rotation * Quaternion.Euler(90f, 0f, 0f), boardRoot);
            ground.name = "BoardGround";

            // The quad prefab is authored 1x1; parenting to a scaled root would double-apply the
            // board scale, so it is sized in world terms and the root's scale divided back out.
            ground.transform.localScale = new Vector3(
                level.boardAspect / boardRoot.localScale.x * boardLength,
                1f / boardRoot.localScale.y * boardLength,
                1f);

            ownedObjects.Add(ground);
        }

        /// <summary>
        /// The visible board: an opaque slab with a coloured zone at each end.
        ///
        /// Kept entirely separate from the NavMesh ground quad above, which stays renderer-off and
        /// collider-on. Decoupling them means the surface the player sees can have real thickness
        /// and extra pieces without any of it becoming pathfinding geometry.
        ///
        /// Everything here is authored in the board root's LOCAL space, where the board is 1.0 long
        /// on z and boardAspect wide on x - the same space LevelDefinition.ToBoardLocal uses - so the
        /// root's uniform scale does the metre conversion and no scale has to be divided back out.
        ///
        /// The slab hangs BELOW the surface (top face at local y=0) so terrain, bases and units all
        /// keep sitting at y=0 exactly as before; only the visible edge is new.
        /// </summary>
        private void BuildBoardSurface(LevelDefinition level, Transform boardRoot)
        {
            if (boardMaterial == null)
            {
                Debug.LogWarning("LevelBuilder: Board Material is not assigned - the board will have no visible surface.", this);
                return;
            }

            float aspect = level.boardAspect;

            var slab = MakeBoardPiece(boardRoot, "BoardSlab", boardColor,
                new Vector3(aspect, boardThickness, 1f),
                new Vector3(0f, -boardThickness * 0.5f, 0f));

            // Zones sit a hair proud of the surface to avoid z-fighting with the slab's top face.
            const float lift = 0.0015f;
            float half = 0.5f - endZoneDepth * 0.5f;

            MakeBoardPiece(boardRoot, "PlayerEndZone", playerZoneColor,
                new Vector3(aspect, lift, endZoneDepth), new Vector3(0f, lift * 0.5f, -half));

            MakeBoardPiece(boardRoot, "EnemyEndZone", enemyZoneColor,
                new Vector3(aspect, lift, endZoneDepth), new Vector3(0f, lift * 0.5f, half));

            BuildDeployZone(aspect, boardRoot, lift);

            if (slab != null) slab.transform.SetSiblingIndex(0);
        }

        /// <summary>
        /// Draws the ground a unit may actually be deployed onto.
        ///
        /// <para><b>Why this is a piece of board art and not just a rule.</b> Deployment used to
        /// accept any tap anywhere on the board, up to and including the square the enemy base
        /// stands on - which made every route, every piece of cover and the whole Covered/Direct
        /// choice optional, because you could simply drop a unit on the objective. Restricting it is
        /// the fix, but an invisible restriction reads as "my tap did nothing", which is this
        /// project's most expensive class of bug. So the enforced zone gets drawn, from the same
        /// number that enforces it.</para>
        ///
        /// <para>The band starts at the FRONT of the coloured player end zone, so the two read as one
        /// staging area rather than two competing stripes, and it ends in a bright line that says
        /// where the ground stops being yours.</para>
        /// </summary>
        private void BuildDeployZone(float aspect, Transform boardRoot, float lift)
        {
            float limit = -0.5f + DeployZoneDepth;
            float bandStart = -0.5f + endZoneDepth;

            // A deploy zone shallower than the end zone would draw a zero- or negative-depth band.
            // The end zone is then already the whole staging area and no extra band is needed.
            // Every zone piece is `lift` tall and centred at lift*0.5, so it spans exactly 0..lift
            // and cannot z-fight the slab's top face - the same trick the end zones use.
            float bandDepth = limit - bandStart;
            if (bandDepth > 0.001f)
            {
                MakeBoardPiece(boardRoot, "DeployZone", deployZoneColor,
                    new Vector3(aspect, lift, bandDepth),
                    new Vector3(0f, lift * 0.5f, bandStart + bandDepth * 0.5f));
            }

            // The limit line straddles the band's forward edge, so it is the one piece that DOES
            // overlap another in z. It gets its own storey rather than a nudged offset.
            MakeBoardPiece(boardRoot, "DeployLimit", deployLimitColor,
                new Vector3(aspect, lift, Mathf.Max(0.001f, deployLimitLineWidth)),
                new Vector3(0f, lift * 1.5f, limit));
        }

        private GameObject MakeBoardPiece(Transform boardRoot, string name, Color color, Vector3 localScale, Vector3 localPosition)
        {
            // Built from the raw cube mesh rather than CreatePrimitive so it never has a Collider in
            // the first place - board decoration must never become collision or NavMesh geometry,
            // and stripping one afterwards would need Destroy, which is illegal in Edit mode.
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.GetComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");

            go.transform.SetParent(boardRoot, worldPositionStays: false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = localScale;

            var renderer = go.GetComponent<Renderer>();
            // A bare MeshRenderer has no material at all, and CreatePrimitive's default is a
            // Built-in-RP one URP renders as magenta - either way the assigned pipeline material is
            // the only thing that reliably draws.
            renderer.sharedMaterial = new Material(boardMaterial) { color = color };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            ownedObjects.Add(go);
            return go;
        }

        private void BuildTerrainPiece(LevelDefinition level, TerrainPlacement placement, Transform boardRoot, float boardLength)
        {
            Vector3 localCentre = level.ToBoardLocal(placement.position);
            Vector3 worldCentre = boardRoot.TransformPoint(localCentre);

            // Normalised size -> metres. Both axes scale by board length so a piece keeps its shape
            // regardless of the board's aspect setting.
            float halfX = placement.size.x * boardLength * 0.5f;
            float halfZ = placement.size.y * boardLength * 0.5f;

            // Corners describe the UNROTATED footprint; the yaw override carries the real angle.
            var data = new TerrainObjectData
            {
                Archetype = placement.archetype,
                Height = placement.height,
                CornerA = worldCentre + new Vector3(-halfX, 0f, -halfZ),
                CornerB = worldCentre + new Vector3(halfX, 0f, halfZ),
                YawOverrideDegrees = boardRoot.eulerAngles.y + placement.rotationDegrees,

                // Heights scale with the board exactly like footprints already do, so a level looks
                // the same on a coffee table and a dining table. The spawner's fixed metre-based
                // category table belongs to the scan flow, where it describes a real measured object.
                HeightOverrideMetres = TerrainObjectSpawner.NormalisedHeightForCategory(placement.height)
                                       * boardLength * level.terrainHeightScale,
            };

            data.Visual = terrainSpawner.Spawn(data);
            if (data.Visual != null) data.Visual.transform.SetParent(boardRoot, worldPositionStays: true);
            if (data.CoverVolume != null) data.CoverVolume.transform.SetParent(boardRoot, worldPositionStays: true);

            spawned.Add(data);
        }

        private Transform BuildBase(GameObject prefab, LevelDefinition level, Vector2 normalised, Transform boardRoot,
                                    float boardLength, string name, int health, out BaseHealth baseHealth)
        {
            baseHealth = null;
            if (prefab == null) return null;

            Vector3 world = boardRoot.TransformPoint(level.ToBoardLocal(normalised));
            var instance = Instantiate(prefab, world, boardRoot.rotation, boardRoot);
            instance.name = name;
            ownedObjects.Add(instance);

            // Board-relative, like terrain footprints and heights. The prefab shipped at 0.3, which
            // under a 0.6m board root came out 18cm across - wider than half the board.
            instance.transform.localScale = Vector3.one * baseFootprint;

            StyleBase(instance, isPlayer: name == "PlayerBase");

            baseHealth = instance.GetComponent<BaseHealth>();
            if (baseHealth == null)
                Debug.LogError($"LevelBuilder: '{name}' prefab has no BaseHealth component - it can never be destroyed.", instance);
            else
                baseHealth.ResetTo(health);

            return instance.transform;
        }

        /// <summary>
        /// Makes ownership readable from the base itself: blue is yours, red is theirs, matching the
        /// coloured end zones underneath them.
        ///
        /// This replaces the prefab's world-space caption, which was the single worst thing on
        /// screen. It was a TextMeshPro at font size 36 on a non-uniformly scaled transform
        /// (0.09, 0.05, 0.3), so it rendered larger than the whole board, stretched, and lying flat
        /// across the battlefield. Colour carries the same information without covering the map, and
        /// the HUD already names the objective in words.
        /// </summary>
        private void StyleBase(GameObject instance, bool isPlayer)
        {
            foreach (var label in instance.GetComponentsInChildren<TMPro.TextMeshPro>(true))
                label.gameObject.SetActive(false);
            foreach (var label in instance.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
                label.gameObject.SetActive(false);

            if (boardMaterial == null) return;

            ScrapSiege.Core.MaterialSlots.Repaint(
                instance, boardMaterial, isPlayer ? playerZoneColor : enemyZoneColor);
        }
    }
}
