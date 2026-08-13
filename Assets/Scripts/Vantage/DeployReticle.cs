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

        [Tooltip("The authored-level flow. When assigned, the reticle is intersected against the " +
                 "board's own transform instead of an ARCore plane - see the note in Update.")]
        [SerializeField] private ScrapSiege.Levels.LevelMatchController levelMatch;

        [Tooltip("Camera the reticle is cast from. Optional - falls back to Camera.main.")]
        [SerializeField] private Camera reticleCamera;

        [Header("Line of sight")]
        [Tooltip("Colour when the aimed point is behind terrain and therefore cannot be deployed " +
                 "onto. Without this the line-of-sight rule is invisible until a tap is refused, " +
                 "and 'my tap did nothing' is the hardest symptom in this project to diagnose.")]
        [SerializeField] private Color blockedColor = new Color(0.90f, 0.30f, 0.28f);

        [Tooltip("Colour when the aimed point is past the forward limit of the player's deploy zone. " +
                 "Deliberately NOT the same red as a blocked sightline: one means 'move so you can " +
                 "see it', the other means 'you can see it fine, it is simply not your ground', and " +
                 "a player who reads them as the same refusal learns the wrong lesson from both.")]
        [SerializeField] private Color outOfZoneColor = new Color(0.55f, 0.56f, 0.62f);

        [SerializeField] private LayerMask sightBlockerMask = 1 << ScrapSiege.Core.SiegeLayers.TerrainOccluder;
        [SerializeField] private float sightNearClip = 0.02f;

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

            if (raycastManager == null && levelMatch == null)
                Debug.LogError("DeployReticle: neither Raycast Manager nor Level Match is assigned - the reticle can never find the table.", this);
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
            if (vantage == null) return;

            var screenCentre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (!TryResolveAimPoint(screenCentre, out Vector3 point))
            {
                if (ring != null) ring.SetActive(false);
                return;
            }

            // Show where the unit would actually end up. Off-NavMesh aim points are rejected by
            // UnitDeploymentController anyway, so dimming here tells the player "this tap will do
            // nothing" before they waste a resource on it.
            bool walkable = NavMesh.SamplePosition(point, out NavMeshHit navHit, 0.15f, NavMesh.AllAreas);
            if (walkable) point = navHit.position;

            // The other half of the same promise: UnitDeploymentController refuses a point it has no
            // sightline to, so the reticle has to show that BEFORE the tap. Checked against the same
            // static rule the reveal system uses, for the same reason - two implementations of "can I
            // see this" would drift, and both fail silently when they do.
            bool visible = !walkable || HasSightlineTo(point);

            // And the third refusal the player has to be able to see coming: reinforcements arrive
            // from your own lines only. The board draws the zone; this says "the thing you are
            // aiming at right now is outside it" without the player having to compare the reticle
            // against a painted line by eye.
            bool inZone = levelMatch == null || levelMatch.IsInDeployZone(point);

            if (!EnsureRing()) return;

            ring.SetActive(true);
            ring.transform.position = point + Vector3.up * ScrapSiege.Core.WorldScale.Metres(surfaceOffset);

            // Radius tracks the real scatter value, so what the player sees IS the rule.
            // ScatterRadius is already board-relative (and so already scaled); only the floor needs
            // converting, or a fully-precise reticle would shrink to an invisible speck.
            float radius = Mathf.Max(vantage.ScatterRadius, ScrapSiege.Core.WorldScale.Metres(minDrawnRadius));
            ring.transform.localScale = new Vector3(radius, 1f, radius);

            Color target = Color.Lerp(preciseColor, looseColor, vantage.Vantage01);
            if (!inZone) target = outOfZoneColor;
            else if (!visible) target = blockedColor;
            else if (!walkable) target = Color.Lerp(target, Color.gray, 0.7f);

            float drawnAlpha = walkable && inZone ? alpha : alpha * 0.4f;
            MaterialFx.SetAlpha(ringMaterial, drawnAlpha);
            ringMaterial.color = new Color(target.r, target.g, target.b, drawnAlpha);
        }

        /// <summary>
        /// Where the device is aimed on the table.
        ///
        /// <para>Intersects the placed board's own transform, mirroring the fix already applied to
        /// <see cref="ScrapSiege.Siege.UnitDeploymentController"/> and
        /// <see cref="ScrapSiege.Siege.RallyController"/>. This was the third component still
        /// requiring a <c>PlaneWithinPolygon</c> AR hit, on a device confirmed to track fine while
        /// never promoting anything to a plane - so on the Tab S6 Lite the precision ring, the one
        /// thing that makes the vantage mechanic legible, would simply never have appeared.</para>
        /// </summary>
        private bool TryResolveAimPoint(Vector2 screenPos, out Vector3 point)
        {
            Transform board = levelMatch != null ? levelMatch.BoardRoot : null;
            Camera cam = ResolveCamera();

            if (board != null && cam != null)
            {
                var plane = new Plane(board.up, board.position);
                Ray ray = cam.ScreenPointToRay(screenPos);
                if (plane.Raycast(ray, out float distance))
                {
                    Vector3 hit = ray.GetPoint(distance);
                    Vector3 local = board.InverseTransformPoint(hit);
                    if (Mathf.Abs(local.x) <= 0.5f && Mathf.Abs(local.z) <= 0.5f)
                    {
                        point = hit;
                        return true;
                    }
                }

                point = default;
                return false;
            }

            if (raycastManager != null && raycastManager.Raycast(screenPos, hits, TrackableType.PlaneWithinPolygon))
            {
                point = hits[0].pose.position;
                return true;
            }

            point = default;
            return false;
        }

        private bool HasSightlineTo(Vector3 point)
        {
            Camera cam = ResolveCamera();
            if (cam == null) return true;

            float lift = ScrapSiege.Core.WorldScale.Metres(0.005f);
            return LineOfSightController.HasClearLine(
                cam.transform.position,
                point + Vector3.up * lift,
                sightBlockerMask,
                ScrapSiege.Core.WorldScale.Metres(sightNearClip));
        }

        private Camera ResolveCamera()
        {
            if (reticleCamera == null) reticleCamera = Camera.main;
            return reticleCamera;
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
