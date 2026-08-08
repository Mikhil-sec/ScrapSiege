using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using ScrapSiege.Vision;

namespace ScrapSiege.Vantage
{
    /// <summary>
    /// Draws the player's *current* deploy precision on the table, before they tap.
    ///
    /// This exists because of real on-device feedback: scatter was working correctly but was
    /// invisible - a unit simply appeared somewhere, and the player could not see how much
    /// uncertainty their posture was buying. A mechanic the player cannot observe cannot be
    /// learned, let alone mastered. The ring makes vantage legible: lean in and it tightens to a
    /// dot, pull back and it widens into a spread you can clearly see is too coarse to thread a gap.
    ///
    /// Follows screen centre rather than the finger, so it reads as a crosshair for where the
    /// device is aimed and updates continuously as the player moves - no touch required.
    /// </summary>
    public class DeployReticle : MonoBehaviour
    {
        [SerializeField] private ARRaycastManager raycastManager;
        [SerializeField] private VantageController vantage;

        [Tooltip("Any material using the active render pipeline - instanced and forced transparent.")]
        [SerializeField] private Material baseMaterial;

        [SerializeField] private Color preciseColor = new Color(0.24f, 0.84f, 0.55f);
        [SerializeField] private Color looseColor = new Color(1f, 0.54f, 0.24f);
        [SerializeField] private float alpha = 0.55f;

        [Tooltip("Ring thickness as a fraction of its radius.")]
        [Range(0.05f, 0.9f)]
        [SerializeField] private float thickness = 0.35f;

        [Tooltip("Smallest drawn radius, so a fully-precise reticle is still a visible dot rather than nothing.")]
        [SerializeField] private float minDrawnRadius = 0.012f;

        [SerializeField] private float surfaceOffset = 0.003f;
        [SerializeField] private int segments = 40;

        private readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();
        private GameObject ring;
        private Material ringMaterial;
        private bool warnedMissingMaterial;

        private void Awake()
        {
            // Only meaningful during Siege; SiegePhaseController enables this alongside deployment.
            enabled = false;

            if (raycastManager == null) Debug.LogError("DeployReticle: Raycast Manager is not assigned - the reticle can never find the table.", this);
            if (vantage == null) Debug.LogError("DeployReticle: Vantage Controller is not assigned - the reticle cannot show deploy precision.", this);
        }

        private void OnDisable()
        {
            if (ring != null) ring.SetActive(false);
        }

        private void OnDestroy()
        {
            if (ringMaterial != null) Destroy(ringMaterial);
            if (ring != null) Destroy(ring);
        }

        private void Update()
        {
            if (raycastManager == null || vantage == null) return;

            var screenCentre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (!raycastManager.Raycast(screenCentre, hits, TrackableType.PlaneWithinPolygon))
            {
                if (ring != null) ring.SetActive(false);
                return;
            }

            Vector3 point = hits[0].pose.position;

            // Show where the unit would actually end up. Off-NavMesh aim points are rejected by
            // UnitDeploymentController anyway, so dimming here tells the player "this tap will do
            // nothing" before they waste a resource on it.
            bool walkable = NavMesh.SamplePosition(point, out NavMeshHit navHit, 0.15f, NavMesh.AllAreas);
            if (walkable) point = navHit.position;

            if (!EnsureRing()) return;

            ring.SetActive(true);
            ring.transform.position = point + Vector3.up * ScrapSiege.Core.WorldScale.Metres(surfaceOffset);

            // Radius tracks the real scatter value, so what the player sees IS the rule.
            // ScatterRadius is already board-relative (and so already scaled); only the floor needs
            // converting, or a fully-precise reticle would shrink to an invisible speck.
            float radius = Mathf.Max(vantage.ScatterRadius, ScrapSiege.Core.WorldScale.Metres(minDrawnRadius));
            ring.transform.localScale = new Vector3(radius, 1f, radius);

            Color target = Color.Lerp(preciseColor, looseColor, vantage.Vantage01);
            if (!walkable) target = Color.Lerp(target, Color.gray, 0.7f);
            MaterialFx.SetAlpha(ringMaterial, walkable ? alpha : alpha * 0.4f);
            ringMaterial.color = new Color(target.r, target.g, target.b, walkable ? alpha : alpha * 0.4f);
        }

        private bool EnsureRing()
        {
            if (ring != null) return true;

            if (baseMaterial == null)
            {
                if (!warnedMissingMaterial)
                {
                    warnedMissingMaterial = true;
                    Debug.LogWarning("DeployReticle: Base Material is not assigned - deploy precision will stay invisible. Assign any URP material.", this);
                }
                return false;
            }

            ring = new GameObject("DeployReticle");
            var filter = ring.AddComponent<MeshFilter>();
            var renderer = ring.AddComponent<MeshRenderer>();
            filter.mesh = BuildRingMesh(Mathf.Max(segments, 8), Mathf.Clamp01(thickness));

            ringMaterial = new Material(baseMaterial);
            MaterialFx.MakeTransparent(ringMaterial);
            renderer.material = ringMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return true;
        }

        /// <summary>
        /// Flat annulus of unit outer radius in the XZ plane, so localScale maps directly to
        /// metres of scatter radius.
        /// </summary>
        private static Mesh BuildRingMesh(int segments, float thickness)
        {
            float inner = 1f - thickness;
            var vertices = new Vector3[segments * 2];
            var triangles = new int[segments * 6];

            for (int i = 0; i < segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                float sin = Mathf.Sin(angle);
                float cos = Mathf.Cos(angle);

                vertices[i * 2] = new Vector3(sin * inner, 0f, cos * inner);
                vertices[i * 2 + 1] = new Vector3(sin, 0f, cos);

                int next = (i + 1) % segments;
                int t = i * 6;
                triangles[t] = i * 2;
                triangles[t + 1] = i * 2 + 1;
                triangles[t + 2] = next * 2 + 1;
                triangles[t + 3] = i * 2;
                triangles[t + 4] = next * 2 + 1;
                triangles[t + 5] = next * 2;
            }

            var mesh = new Mesh { name = "DeployReticleRing" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
