using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace ScrapSiege.Terrain
{
    /// <summary>
    /// "Cartoonify" step (plan.md Mechanic 1): places an opaque placeholder primitive over
    /// the real object's measured position, scaled at least as large as the footprint so the
    /// real object never peeks out. Placeholder colors only for now - swap in real textured
    /// art here once available (plan.md Week 5 polish); nothing downstream (pathing, gameplay)
    /// depends on the visual.
    /// </summary>
    public class TerrainObjectSpawner : MonoBehaviour
    {
        [SerializeField] private float marginMultiplier = 1.15f;

        [Tooltip("Any opaque material using the project's active render pipeline shader - colors are instanced from this per archetype. Create via Project window > Create > Material.")]
        [SerializeField] private Material baseMaterial;

        // A near-square/round footprint (aspect ratio close to 1) reads better as a cylinder
        // than a cube - e.g. a hairgel bottle or mug should not render as a rectangular block.
        [SerializeField] private float roundnessAspectRatioThreshold = 1.3f;

        // How far past the object's own footprint the CoverLane NavMesh area extends - wide
        // enough that a unit passing alongside (not just on top of) the object still counts as
        // covered for GarrisonSentry's exposure check.
        [SerializeField] private float coverLaneMargin = 0.25f;
        [SerializeField] private float coverLaneVolumeHeight = 1f;

        [Header("Height category -> world height (meters)")]
        [SerializeField] private float shortHeight = 0.06f;
        [SerializeField] private float mediumHeight = 0.15f;
        [SerializeField] private float tallHeight = 0.30f;

        public GameObject Spawn(TerrainObjectData data)
        {
            var primitive = PrimitiveForArchetype(data.Archetype, data.AspectRatio, roundnessAspectRatioThreshold);
            var go = GameObject.CreatePrimitive(primitive);
            go.name = $"Terrain_{data.Archetype}";

            float height = HeightForCategory(data.Height);
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
                    if (!data.LongAxisIsX)
                        go.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                    break;
            }

            // Every terrain object blocks Siege pathing the instant it's placed - carving
            // updates live against the one baked NavMesh, so Undo/Delete during Fortify never
            // require a rebake.
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

            var modifier = coverVolumeGO.AddComponent<NavMeshModifierVolume>();
            modifier.area = NavMeshAreas.CoverAreaIndex;
            modifier.size = new Vector3(sizeX + coverLaneMargin * 2f, coverLaneVolumeHeight, sizeZ + coverLaneMargin * 2f);
            // Volume's transform.position is already at table height (data.Center), so this
            // offset just extends the volume upward from the table surface, not around data.Center.y again.
            modifier.center = new Vector3(0f, coverLaneVolumeHeight * 0.5f, 0f);

            return coverVolumeGO;
        }

        private float HeightForCategory(HeightCategory category)
        {
            switch (category)
            {
                case HeightCategory.Short: return shortHeight;
                case HeightCategory.Tall: return tallHeight;
                default: return mediumHeight;
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
    }
}
