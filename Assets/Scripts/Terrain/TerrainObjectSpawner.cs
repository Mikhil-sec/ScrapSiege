using UnityEngine;

namespace ScrapSiege.Terrain
{
    /// <summary>
    /// "Cartoonify" step (plan.md Mechanic 1): places an opaque placeholder primitive over
    /// the real object's measured position, scaled at least as large as the footprint so the
    /// real object never peeks out. Swap the primitives here for real art once available -
    /// nothing downstream (pathing, gameplay) depends on the visual.
    /// </summary>
    public class TerrainObjectSpawner : MonoBehaviour
    {
        [SerializeField] private float marginMultiplier = 1.15f;

        [Header("Height category -> world height (meters)")]
        [SerializeField] private float shortHeight = 0.06f;
        [SerializeField] private float mediumHeight = 0.15f;
        [SerializeField] private float tallHeight = 0.30f;

        public GameObject Spawn(TerrainObjectData data)
        {
            var primitive = PrimitiveForArchetype(data.Archetype);
            var go = GameObject.CreatePrimitive(primitive);
            go.name = $"Terrain_{data.Archetype}";

            float height = HeightForCategory(data.Height);
            float sizeX = Mathf.Max(data.FootprintX, 0.05f) * marginMultiplier;
            float sizeZ = Mathf.Max(data.FootprintZ, 0.05f) * marginMultiplier;

            go.transform.position = data.Center + Vector3.up * (height * 0.5f);

            switch (primitive)
            {
                case PrimitiveType.Cylinder:
                    // Unity's default cylinder is 2 units tall with a 1-unit diameter.
                    float diameter = Mathf.Max(sizeX, sizeZ);
                    go.transform.localScale = new Vector3(diameter, height * 0.5f, diameter);
                    break;

                case PrimitiveType.Cube:
                default:
                    go.transform.localScale = new Vector3(sizeX, height, sizeZ);
                    if (!data.LongAxisIsX)
                        go.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                    break;
            }

            return go;
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

        private static PrimitiveType PrimitiveForArchetype(TerrainArchetype archetype)
        {
            switch (archetype)
            {
                case TerrainArchetype.SpireChokepoint:
                case TerrainArchetype.Watchtower:
                    return PrimitiveType.Cylinder;
                case TerrainArchetype.WallBarricade:
                case TerrainArchetype.RubbleCover:
                case TerrainArchetype.PlainObstacle:
                default:
                    return PrimitiveType.Cube;
            }
        }
    }
}
