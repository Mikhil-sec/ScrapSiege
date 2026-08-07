using UnityEngine;

namespace ScrapSiege.Core
{
    /// <summary>
    /// Terrain occluders live on their own layer so line-of-sight raycasts can hit walls and
    /// spires without also hitting the synthetic ground quad (which every camera-to-table ray
    /// grazes) or the units themselves. Index 8 is the first free user layer in this project -
    /// 0-7 are Unity's reserved/built-in block, and 30 is XR Simulation.
    /// </summary>
    public static class SiegeLayers
    {
        public const int TerrainOccluder = 8;
        public const string TerrainOccluderName = "SiegeTerrain";

        public static int TerrainOccluderMask => 1 << TerrainOccluder;
    }
}
