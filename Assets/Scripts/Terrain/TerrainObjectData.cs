using UnityEngine;

namespace ScrapSiege.Terrain
{
    /// <summary>
    /// One scavenged real-world object's measured footprint, as captured during Fortify.
    /// Same shape regardless of which detection tier produced it (plan.md Mechanic 1).
    /// </summary>
    public class TerrainObjectData
    {
        public Vector3 CornerA;
        public Vector3 CornerB;
        public HeightCategory Height;
        public TerrainArchetype Archetype;

        /// <summary>The spawned placeholder GameObject for this object, set after TerrainObjectSpawner.Spawn.</summary>
        public GameObject Visual;

        /// <summary>The CoverLane NavMesh tagging volume, if this archetype creates one (RubbleCover/WallBarricade). Null otherwise.</summary>
        public GameObject CoverVolume;

        /// <summary>
        /// Explicit world yaw for the spawned visual. Set by authored levels, which place pieces at
        /// arbitrary angles; left null by the scan/Fortify flow, where orientation is inferred from
        /// which footprint axis is longer. Corners still describe the *unrotated* footprint, so
        /// FootprintX/Z stay meaningful as width and depth.
        /// </summary>
        public float? YawOverrideDegrees;

        /// <summary>
        /// Explicit world height in metres for the spawned visual, overriding the fixed
        /// <see cref="HeightCategory"/> table. Set by authored levels so a piece's height scales with
        /// the board the same way its footprint already does - without this a "Tall" piece is a fixed
        /// 0.30m regardless of board size, which on a 0.60m board is half the board's length. Left
        /// null by the scan/Fortify flow, where the categories describe a real measured object.
        /// </summary>
        public float? HeightOverrideMetres;

        public Vector3 Center => (CornerA + CornerB) * 0.5f;

        /// <summary>Footprint extent along world X.</summary>
        public float FootprintX => Mathf.Abs(CornerB.x - CornerA.x);

        /// <summary>Footprint extent along world Z.</summary>
        public float FootprintZ => Mathf.Abs(CornerB.z - CornerA.z);

        public float FootprintArea => Mathf.Max(FootprintX, 0.01f) * Mathf.Max(FootprintZ, 0.01f);

        /// <summary>Long side / short side. 1.0 = square, larger = more elongated.</summary>
        public float AspectRatio
        {
            get
            {
                float major = Mathf.Max(FootprintX, FootprintZ);
                float minor = Mathf.Max(Mathf.Min(FootprintX, FootprintZ), 0.01f);
                return major / minor;
            }
        }

        /// <summary>True if the footprint's long axis runs along world X rather than Z.</summary>
        public bool LongAxisIsX => FootprintX >= FootprintZ;
    }
}
