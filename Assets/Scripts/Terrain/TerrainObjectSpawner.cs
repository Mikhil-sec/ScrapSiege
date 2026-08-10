using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using ScrapSiege.Core;
using ScrapSiege.Monetization;

namespace ScrapSiege.Terrain
{
    /// <summary>
    /// "Cartoonify" step (plan.md Mechanic 1): places an opaque placeholder primitive over
    /// the real object's measured position, scaled at least as large as the footprint so the
    /// real object never peeks out. Placeholder colors only for now - swap in real textured
    /// art here once available (plan.md Week 5 polish); nothing downstream (pathing, gameplay)
    /// depends on the visual. Color palette is the one Pro cosmetic unlock built alongside the
    /// RevenueCat integration - reads ProEntitlement.IsUnlocked (a decoupled gate, not the
    /// RevenueCat SDK directly - see ProEntitlement.cs for why).
    /// </summary>
    public class TerrainObjectSpawner : MonoBehaviour
    {
        [SerializeField] private float marginMultiplier = 1.15f;

        [Tooltip("Any opaque material using the project's active render pipeline shader - colors are instanced from this per archetype. Create via Project window > Create > Material.")]
        [SerializeField] private Material baseMaterial;

        [Header("Low-poly models (Assets/Models)")]
        [Tooltip("Each model is authored to fill a unit cube with its base at y=0, so the footprint " +
                 "scaling below maps straight to metres. Leave any of these empty to fall back to " +
                 "the original primitive for that archetype.")]
        [SerializeField] private GameObject wallModel;
        [SerializeField] private GameObject spireModel;
        [SerializeField] private GameObject watchtowerModel;
        [SerializeField] private GameObject rubbleModel;
        [SerializeField] private GameObject plainObstacleModel;

        // A near-square/round footprint (aspect ratio close to 1) reads better as a cylinder
        // than a cube - e.g. a hairgel bottle or mug should not render as a rectangular block.
        [SerializeField] private float roundnessAspectRatioThreshold = 1.3f;

        // How far past the object's own footprint the CoverLane NavMesh area extends - wide
        // enough that a unit passing alongside (not just on top of) the object still counts as
        // covered for GarrisonSentry's exposure check.
        //
        // Reduced from 0.25 to 0.05 for authored maps: 0.25m *per side* turned a 5cm cover piece
        // into a 55cm-wide safe lane, which on a 60cm board is the entire table. That made cover
        // free and unmissable, so precise placement bought the player nothing. At 0.05 the lane is
        // a real corridor you have to actually land in - which is what gives the vantage mechanic
        // something to be precise *about*. Retest Covered-vs-Direct routing after changing this.
        // REAL metres - converted through WorldScale where used.
        [SerializeField] private float coverLaneMargin = 0.05f;
        [SerializeField] private float coverLaneVolumeHeight = 1f;

        // REAL metres, and only used by the legacy scan/Fortify flow, where they describe an actual
        // measured object on the table. Authored levels go through NormalisedHeightForCategory
        // instead, which is already a fraction of board length.
        [Header("Height category -> world height (REAL meters)")]
        [SerializeField] private float shortHeight = 0.06f;
        [SerializeField] private float mediumHeight = 0.15f;
        [SerializeField] private float tallHeight = 0.30f;

        public GameObject Spawn(TerrainObjectData data)
        {
            GameObject model = ModelForArchetype(data.Archetype);
            return model != null ? SpawnFromModel(data, model) : SpawnFromPrimitive(data);
        }

        /// <summary>
        /// Instantiates the authored low-poly model for this archetype.
        ///
        /// Every model is built to fill a unit cube with its base at y=0, which is why this needs
        /// no per-model offsets: localScale IS the real-world size in metres, and the object sits
        /// on the table by construction. Primitives are centred instead, which is the whole reason
        /// the older path below has to add half a height to its position.
        ///
        /// The model is parented to a container this method owns rather than being transformed
        /// directly. An imported FBX root carries an axis-correction rotation (Blender authors Z-up,
        /// Unity is Y-up, so the importer hands back a root rotated -90 on X). Writing
        /// transform.rotation straight onto the instantiated model destroyed that correction, which
        /// laid every terrain piece on its side and sank it half-way into the board - and because
        /// localScale was still (width, height, depth), the height was then applied along the
        /// model's depth axis, mangling its proportions too. Keeping placement on a container means
        /// the level's yaw and the model's import correction can never fight again, whatever a
        /// future re-export happens to do.
        /// </summary>
        private GameObject SpawnFromModel(TerrainObjectData data, GameObject model)
        {
            var go = new GameObject($"Terrain_{data.Archetype}");

            var visual = Instantiate(model, go.transform);
            visual.name = "Visual";

            if (BlocksLineOfSight(data.Archetype))
                SetLayerRecursively(go, SiegeLayers.TerrainOccluder);

            float height = HeightFor(data);
            float sizeX = Mathf.Max(data.FootprintX, 0.05f) * marginMultiplier;
            float sizeZ = Mathf.Max(data.FootprintZ, 0.05f) * marginMultiplier;

            go.transform.position = data.Center;
            go.transform.localScale = new Vector3(sizeX, height, sizeZ);

            if (data.YawOverrideDegrees.HasValue)
                go.transform.rotation = Quaternion.Euler(0f, data.YawOverrideDegrees.Value, 0f);
            else if (!data.LongAxisIsX)
                go.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            // FBX imports carry no collider, but line of sight raycasts against colliders - without
            // one, terrain would be visible yet completely transparent to sight, silently disabling
            // Mechanic 2. A box matching the unit cube is enough at this scale and far cheaper than
            // a mesh collider. Lives on the container, whose axes are the placement axes.
            var box = go.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.5f, 0f);
            box.size = Vector3.one;

            if (BlocksMovement(data.Archetype))
            {
                var obstacle = go.AddComponent<NavMeshObstacle>();
                obstacle.carving = true;
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.center = new Vector3(0f, 0.5f, 0f);
                obstacle.size = Vector3.one;
            }

            ApplyArchetypeColor(go, data.Archetype);

            if (data.Archetype == TerrainArchetype.RubbleCover || data.Archetype == TerrainArchetype.WallBarricade)
                data.CoverVolume = TagCoverLane(data, sizeX, sizeZ);

            return go;
        }

        private GameObject ModelForArchetype(TerrainArchetype archetype)
        {
            switch (archetype)
            {
                case TerrainArchetype.WallBarricade: return wallModel;
                case TerrainArchetype.SpireChokepoint: return spireModel;
                case TerrainArchetype.Watchtower: return watchtowerModel;
                case TerrainArchetype.RubbleCover: return rubbleModel;
                case TerrainArchetype.PlainObstacle: return plainObstacleModel;
                default: return null;
            }
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursively(child.gameObject, layer);
        }

        /// <summary>
        /// Recolours the model per material slot. Colour is the gameplay signal (it tells the player
        /// what a piece does) and it is where the Pro cosmetic palette lives, so it has to win over
        /// whatever the Blender material said - but flattening *every* slot to one flat colour, as
        /// this used to, erased all the shape-reading detail the models carry and left each piece an
        /// unreadable single-colour blob.
        ///
        /// Slots are interpreted by name (see <see cref="MaterialSlots"/>): the body takes the
        /// archetype colour, accents take a darkened version of it so the silhouette still reads as
        /// one object, and base plates / bare metal stay neutral so they read as the same "material"
        /// across every archetype. A model with a single unnamed slot behaves exactly as before.
        /// </summary>
        private void ApplyArchetypeColor(GameObject go, TerrainArchetype archetype)
        {
            if (baseMaterial == null) return;

            foreach (var renderer in go.GetComponentsInChildren<Renderer>())
            {
                var sources = renderer.sharedMaterials;
                var instances = new Material[sources.Length];
                for (int i = 0; i < sources.Length; i++)
                    instances[i] = MaterialFor(archetype, MaterialSlots.RoleForSlot(sources[i]));

                renderer.sharedMaterials = instances;
            }
        }

        // One material per (archetype, role) rather than one per spawned object. The old code
        // allocated a fresh Material for every piece on every build and never released them, which
        // leaked a little on each level restart.
        //
        // The Pro state is deliberately NOT part of this key. It used to be, which meant a purchase
        // completed mid-match changed which cache entry *new* lookups landed on while every
        // already-spawned renderer kept holding the free-palette material - so the one shipped Pro
        // cosmetic silently did nothing until the player restarted into a fresh match. Keying on
        // (archetype, role) alone means there is exactly one material per slot for the lifetime of
        // the board, and switching palette is just recolouring those materials in place - every
        // renderer already points at them, so the whole board repaints on the same frame.
        private readonly System.Collections.Generic.Dictionary<int, Material> materialCache
            = new System.Collections.Generic.Dictionary<int, Material>();

        // The primitive fallback path (SpawnFromPrimitive) instances its own material per object
        // rather than going through the cache, so those are tracked separately - with the archetype
        // that decides their colour - to be repainted and released alongside it.
        private struct PrimitiveMaterial
        {
            public Material Material;
            public TerrainArchetype Archetype;
        }

        private readonly System.Collections.Generic.List<PrimitiveMaterial> primitiveMaterials
            = new System.Collections.Generic.List<PrimitiveMaterial>();

        private const int RoleStride = 8;

        private Material MaterialFor(TerrainArchetype archetype, MaterialSlots.Role role)
        {
            int key = (int)archetype * RoleStride + (int)role;
            if (materialCache.TryGetValue(key, out var cached) && cached != null) return cached;

            var created = new Material(baseMaterial) { color = ColorForCacheKey(key) };
            materialCache[key] = created;
            return created;
        }

        private static Color ColorForCacheKey(int key)
        {
            var archetype = (TerrainArchetype)(key / RoleStride);
            var role = (MaterialSlots.Role)(key % RoleStride);
            return MaterialSlots.ColorForRole(role, ColorForArchetype(archetype));
        }

        private void OnEnable()
        {
            ProEntitlement.Changed += OnProEntitlementChanged;
        }

        private void OnDisable()
        {
            ProEntitlement.Changed -= OnProEntitlementChanged;
        }

        /// <summary>
        /// Repaints every material this spawner owns when the Pro entitlement flips, so a purchase
        /// (or a restore, or an expiry) is visible immediately on the board the player is looking at
        /// instead of only on the next match. Cheap: one colour write per (archetype, role) slot,
        /// not per spawned object.
        /// </summary>
        private void OnProEntitlementChanged(bool unlocked)
        {
            foreach (var entry in materialCache)
                if (entry.Value != null) entry.Value.color = ColorForCacheKey(entry.Key);

            // Primitive-path objects are single-slot, so they take the body colour directly.
            // Iterated backwards so destroyed entries can be dropped in the same pass.
            for (int i = primitiveMaterials.Count - 1; i >= 0; i--)
            {
                var entry = primitiveMaterials[i];
                if (entry.Material == null) primitiveMaterials.RemoveAt(i);
                else entry.Material.color = ColorForArchetype(entry.Archetype);
            }

            Debug.Log($"TerrainObjectSpawner: Pro entitlement {(unlocked ? "unlocked" : "lost")} - repainted {materialCache.Count} cached and {primitiveMaterials.Count} primitive material(s).");
        }

        private void OnDestroy()
        {
            ProEntitlement.Changed -= OnProEntitlementChanged;

            foreach (var material in materialCache.Values)
                if (material != null) Destroy(material);
            materialCache.Clear();

            foreach (var entry in primitiveMaterials)
                if (entry.Material != null) Destroy(entry.Material);
            primitiveMaterials.Clear();
        }

        /// <summary>
        /// Original primitive path, kept as the fallback for any archetype with no model assigned.
        /// </summary>
        private GameObject SpawnFromPrimitive(TerrainObjectData data)
        {
            var primitive = PrimitiveForArchetype(data.Archetype, data.AspectRatio, roundnessAspectRatioThreshold);
            var go = GameObject.CreatePrimitive(primitive);
            go.name = $"Terrain_{data.Archetype}";

            // Sight-blocking archetypes go on the occluder layer so LineOfSightController's
            // raycasts hit them and nothing else. RubbleCover is deliberately excluded - plan.md
            // defines it as passable cover that "blocks nothing visually", so it must affect
            // pathing and garrison exposure without ever hiding an enemy.
            if (BlocksLineOfSight(data.Archetype))
                go.layer = SiegeLayers.TerrainOccluder;

            float height = HeightFor(data);
            float sizeX = Mathf.Max(data.FootprintX, 0.05f) * marginMultiplier;
            float sizeZ = Mathf.Max(data.FootprintZ, 0.05f) * marginMultiplier;

            go.transform.position = data.Center + Vector3.up * (height * 0.5f);

            float diameter = Mathf.Max(sizeX, sizeZ);

            switch (primitive)
            {
                case PrimitiveType.Cylinder:
                    // Unity's default cylinder is 2 units tall with a 1-unit diameter.
                    go.transform.localScale = new Vector3(diameter, height * 0.5f, diameter);
                    break;

                case PrimitiveType.Cube:
                default:
                    go.transform.localScale = new Vector3(sizeX, height, sizeZ);
                    // Authored levels dictate their own angle; the scan flow infers one from the
                    // footprint's long axis. A cylinder is radially symmetric so neither applies.
                    if (data.YawOverrideDegrees.HasValue)
                        go.transform.rotation = Quaternion.Euler(0f, data.YawOverrideDegrees.Value, 0f);
                    else if (!data.LongAxisIsX)
                        go.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                    break;
            }

            // Blocking terrain obstructs Siege pathing the instant it's placed - carving updates
            // live against the one baked NavMesh, so Undo/Delete during Fortify never require a
            // rebake. Rubble is deliberately exempt (see BlocksMovement): it is passable cover.
            if (BlocksMovement(data.Archetype))
            {
                var obstacle = go.AddComponent<NavMeshObstacle>();
                obstacle.carving = true;
                if (primitive == PrimitiveType.Cylinder)
                {
                    // Radius/Height are local-space, scaled by the transform like a CapsuleCollider -
                    // use the default primitive's own unit dimensions (0.5 radius, 2 height) and let
                    // the already-applied localScale do the real-world sizing, same as the Box case.
                    obstacle.shape = NavMeshObstacleShape.Capsule;
                    obstacle.radius = 0.5f;
                    obstacle.height = 2f;
                }
                else
                {
                    obstacle.shape = NavMeshObstacleShape.Box;
                    obstacle.size = Vector3.one; // local scale already carries the true footprint/height
                }
            }

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null && baseMaterial != null)
            {
                // CreatePrimitive() assigns a Built-in-RP Standard material, which URP can't
                // render (shows as flat magenta/purple regardless of color) - instance from a
                // known-working material (assigned in the Inspector) instead of a runtime
                // Shader.Find lookup, which depends on the shader surviving build stripping.
                var material = new Material(baseMaterial);
                material.color = ColorForArchetype(data.Archetype);
                renderer.material = material;

                // Tracked so a Pro entitlement change repaints (and OnDestroy releases) it, the
                // same as the cached model-path materials.
                primitiveMaterials.Add(new PrimitiveMaterial { Material = material, Archetype = data.Archetype });
            }

            if (data.Archetype == TerrainArchetype.RubbleCover || data.Archetype == TerrainArchetype.WallBarricade)
                data.CoverVolume = TagCoverLane(data, sizeX, sizeZ);

            return go;
        }

        /// <summary>
        /// Route-variety mechanic: marks the ground around cover-type terrain with a NavMesh
        /// area a "Covered" deploy route can detour through (see NavMeshAreas). Must run before
        /// SiegePhaseController bakes the NavMesh, which is guaranteed since all Fortify spawning
        /// happens before StartSiege(). A standalone GameObject (not parented to the terrain
        /// visual) so its size isn't affected by the visual's own transform.localScale - caller
        /// stores the returned object on TerrainObjectData.CoverVolume so Undo/Delete can clean
        /// it up alongside the visual instead of leaking an orphaned cover tag.
        /// </summary>
        private GameObject TagCoverLane(TerrainObjectData data, float sizeX, float sizeZ)
        {
            var coverVolumeGO = new GameObject($"CoverLane_{data.Archetype}");
            coverVolumeGO.transform.position = data.Center;

            // Match the visual's angle, otherwise a rotated wall lays down an axis-aligned cover
            // lane that no longer lines up with the cover the player can see.
            if (data.YawOverrideDegrees.HasValue)
                coverVolumeGO.transform.rotation = Quaternion.Euler(0f, data.YawOverrideDegrees.Value, 0f);

            var modifier = coverVolumeGO.AddComponent<NavMeshModifierVolume>();
            modifier.area = NavMeshAreas.CoverAreaIndex;
            // sizeX/sizeZ already arrive in scaled world units (they come from board length), so the
            // margin has to be converted too - otherwise the safe lane silently narrows by the world
            // scale factor relative to the board, undoing the 0.25 -> 0.05 tuning pass.
            float margin = WorldScale.Metres(coverLaneMargin);
            modifier.size = new Vector3(sizeX + margin * 2f, WorldScale.Metres(coverLaneVolumeHeight), sizeZ + margin * 2f);
            // Volume's transform.position is already at table height (data.Center), so this
            // offset just extends the volume upward from the table surface, not around data.Center.y again.
            modifier.center = new Vector3(0f, coverLaneVolumeHeight * 0.5f, 0f);

            return coverVolumeGO;
        }

        /// <summary>
        /// Which archetypes hide what is behind them. Matches plan.md's archetype table: Wall,
        /// Spire and Watchtower are hard blocks that block line of sight; PlainObstacle is a hard
        /// block but low, so it still occludes; RubbleCover blocks nothing visually.
        /// </summary>
        /// <summary>
        /// Which archetypes physically block movement, i.e. carve a hole in the NavMesh.
        ///
        /// plan.md's archetype table lists Rubble/Cover as "Passable, lays a CoverLane" - it is the
        /// lane a unit routes *through*, not a wall. Every archetype used to carve unconditionally,
        /// which made the cover corridor solid: on The Narrows the rubble line and the wall spine
        /// together left a 5mm gap on a 33cm-wide board, so after the bake's agent-radius erosion the
        /// two halves of the map were completely severed and NO route to the enemy base existed for
        /// either deploy mode. That is what "the trooper walks a bit then stops forever" really was.
        ///
        /// Note this is independent of <see cref="BlocksLineOfSight"/>: rubble blocks neither sight
        /// nor movement, but still lays down the cheap CoverLane area that Covered mode steers by
        /// and that GarrisonSentry treats as safe.
        /// </summary>
        public static bool BlocksMovement(TerrainArchetype archetype)
        {
            return archetype != TerrainArchetype.RubbleCover;
        }

        public static bool BlocksLineOfSight(TerrainArchetype archetype)
        {
            switch (archetype)
            {
                case TerrainArchetype.WallBarricade:
                case TerrainArchetype.SpireChokepoint:
                case TerrainArchetype.Watchtower:
                case TerrainArchetype.PlainObstacle:
                    return true;

                case TerrainArchetype.RubbleCover:
                default:
                    return false;
            }
        }

        /// <summary>
        /// Authored levels supply an explicit height scaled to the board they were placed on; the
        /// scan/Fortify flow has no board to scale against and falls back to the fixed category
        /// table, which describes a real measured object.
        /// </summary>
        private float HeightFor(TerrainObjectData data)
        {
            return data.HeightOverrideMetres ?? HeightForCategory(data.Height);
        }

        private float HeightForCategory(HeightCategory category)
        {
            switch (category)
            {
                case HeightCategory.Short: return WorldScale.Metres(shortHeight);
                case HeightCategory.Tall: return WorldScale.Metres(tallHeight);
                default: return WorldScale.Metres(mediumHeight);
            }
        }

        /// <summary>
        /// Height as a fraction of board length, for authored levels. Sized against the ~3cm unit:
        /// Short is knee-high cover, Medium hides a standing unit, Tall reads as a landmark. The
        /// absolute values above cannot be reused here - a fixed 0.30m "Tall" piece on a 0.60m board
        /// is half the board's own length, which is what made authored maps look so wrong.
        /// </summary>
        public static float NormalisedHeightForCategory(HeightCategory category)
        {
            switch (category)
            {
                case HeightCategory.Short: return 0.035f;
                case HeightCategory.Tall: return 0.130f;
                default: return 0.070f;
            }
        }

        private static PrimitiveType PrimitiveForArchetype(TerrainArchetype archetype, float aspectRatio, float roundnessThreshold)
        {
            switch (archetype)
            {
                case TerrainArchetype.SpireChokepoint:
                case TerrainArchetype.Watchtower:
                    return PrimitiveType.Cylinder;

                case TerrainArchetype.WallBarricade:
                    return PrimitiveType.Cube;

                case TerrainArchetype.RubbleCover:
                case TerrainArchetype.PlainObstacle:
                default:
                    return aspectRatio <= roundnessThreshold ? PrimitiveType.Cylinder : PrimitiveType.Cube;
            }
        }

        private static Color ColorForArchetype(TerrainArchetype archetype)
        {
            return ProEntitlement.IsUnlocked
                ? ProColorForArchetype(archetype)
                : FreeColorForArchetype(archetype);
        }

        private static Color FreeColorForArchetype(TerrainArchetype archetype)
        {
            switch (archetype)
            {
                case TerrainArchetype.WallBarricade: return new Color(0.55f, 0.4f, 0.25f);
                case TerrainArchetype.SpireChokepoint: return new Color(0.5f, 0.5f, 0.55f);
                case TerrainArchetype.RubbleCover: return new Color(0.6f, 0.55f, 0.3f);
                case TerrainArchetype.Watchtower: return new Color(0.7f, 0.15f, 0.15f);
                case TerrainArchetype.PlainObstacle:
                default: return new Color(0.4f, 0.6f, 0.4f);
            }
        }

        // Pro cosmetic palette (the "Scrap Siege Pro" subscription's testable IAP) - a distinct,
        // more saturated look. Same archetypes, same gameplay, purely a reskin - matches
        // plan.md Section 6's "genuine value-tier gating, not functionality lockout" design.
        private static Color ProColorForArchetype(TerrainArchetype archetype)
        {
            switch (archetype)
            {
                case TerrainArchetype.WallBarricade: return new Color(0.85f, 0.55f, 0.15f);
                case TerrainArchetype.SpireChokepoint: return new Color(0.25f, 0.55f, 0.95f);
                case TerrainArchetype.RubbleCover: return new Color(0.9f, 0.8f, 0.2f);
                case TerrainArchetype.Watchtower: return new Color(0.9f, 0.1f, 0.55f);
                case TerrainArchetype.PlainObstacle:
                default: return new Color(0.2f, 0.85f, 0.5f);
            }
        }
    }
}
