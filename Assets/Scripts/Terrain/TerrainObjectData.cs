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
