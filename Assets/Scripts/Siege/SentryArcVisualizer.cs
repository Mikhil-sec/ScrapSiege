using System.Collections.Generic;
using UnityEngine;
using ScrapSiege.Vision;

namespace ScrapSiege.Siege
{
    /// <summary>
    /// Draws a sentry's covered arc as a flat fan on the table surface.
    ///
    /// Mechanic 4 (flank by walking) is invisible without this: a sentry that covers a facing arc
    /// rather than a full circle is only tactically legible if the player can physically see which
    /// way it is pointing. Drawn in the world rather than on the HUD deliberately - walking around
    /// the table until the wedge points away from you teaches the mechanic with no tutorial text.
    ///
    /// Mesh is generated once at Awake from the sentry's own arc settings, so tuning the arc in
    /// the Inspector keeps the visual honest automatically.
    /// </summary>
    [RequireComponent(typeof(GarrisonSentry))]
    public class SentryArcVisualizer : MonoBehaviour
    {
        [Tooltip("Any material using the active render pipeline - instanced and forced transparent. " +
                 "Assigned rather than Shader.Find'd because runtime shader lookup is unreliable in " +
                 "built players (see CLAUDE.md gotchas).")]
        [SerializeField] private Material arcBaseMaterial;

        [SerializeField] private Color arcColor = new Color(0.9f, 0.25f, 0.2f);
        [SerializeField] private float arcAlpha = 0.18f;

        [Tooltip("Lifts the fan just off the table so it doesn't z-fight with the ground quad.")]
        [SerializeField] private float surfaceOffset = 0.002f;

        [Tooltip("Triangles in the fan. 24 is plenty smooth at tabletop scale.")]
        [SerializeField] private int segments = 24;

        private GameObject arcObject;

        private void Awake()
        {
            var sentry = GetComponent<GarrisonSentry>();
            if (sentry == null) return;

            if (arcBaseMaterial == null)
            {
                Debug.LogWarning("SentryArcVisualizer: Arc Base Material is not assigned - the sentry's covered arc will be invisible, which hides the flanking mechanic from the player.", this);
                return;
            }

            BuildArc(sentry.DetectionRadius, sentry.FacingArcDegrees);
        }

        private void OnDestroy()
        {
            if (arcObject != null) Destroy(arcObject);
        }

        private void BuildArc(float radius, float arcDegrees)
        {
            arcObject = new GameObject("SentryArc");

            // Parented so it inherits the sentry's rotation - rotating the sentry rotates the
            // wedge - but positioned flat at the sentry's own height on the table.
            arcObject.transform.SetParent(transform, worldPositionStays: false);
            arcObject.transform.localPosition = new Vector3(0f, surfaceOffset, 0f);
            arcObject.transform.localRotation = Quaternion.identity;

            var filter = arcObject.AddComponent<MeshFilter>();
            var renderer = arcObject.AddComponent<MeshRenderer>();

            filter.mesh = BuildFanMesh(radius, arcDegrees, Mathf.Max(segments, 3));

            var material = new Material(arcBaseMaterial) { color = arcColor };
            MaterialFx.MakeTransparent(material);
            MaterialFx.SetAlpha(material, arcAlpha);
            renderer.material = material;

            // A decal on the floor should not cast or receive shadows - it would read as a solid
            // object rather than a marking.
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>
        /// Triangle fan in the XZ plane, centred on the origin and bisected by +Z (the sentry's
        /// forward), spanning arcDegrees total.
        /// </summary>
        private static Mesh BuildFanMesh(float radius, float arcDegrees, int segments)
        {
            var vertices = new List<Vector3>(segments + 2) { Vector3.zero };
            var triangles = new List<int>(segments * 3);

            float half = arcDegrees * 0.5f;
            float step = arcDegrees / segments;

            for (int i = 0; i <= segments; i++)
            {
                float angle = -half + step * i;
                float radians = angle * Mathf.Deg2Rad;

                // Sin on X and cos on Z puts angle 0 along +Z, matching transform.forward.
                vertices.Add(new Vector3(Mathf.Sin(radians) * radius, 0f, Mathf.Cos(radians) * radius));
            }

            for (int i = 1; i <= segments; i++)
            {
                triangles.Add(0);
                triangles.Add(i);
                triangles.Add(i + 1);
            }

            var mesh = new Mesh { name = "SentryArcFan" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
