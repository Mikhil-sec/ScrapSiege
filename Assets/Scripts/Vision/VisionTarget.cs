using System.Collections.Generic;
using UnityEngine;

namespace ScrapSiege.Vision
{
    /// <summary>
    /// Marks something the player can only see by actually looking at it. Attach to enemy
    /// garrison sentries, the enemy base, and (once it exists) every AI-commanded unit.
    ///
    /// Registers itself into a static list the same way SiegeUnit does, so LineOfSightController
    /// never has to search the scene and new targets spawned mid-match are picked up automatically.
    /// </summary>
    public class VisionTarget : MonoBehaviour
    {
        [Tooltip("Derive the three sample points from this object's renderer bounds. Leave on - it " +
                 "self-tunes to any prefab, which matters because these are tabletop-scale objects " +
                 "(a 5cm sphere) where a hand-typed height silently samples empty air above the unit.")]
        [SerializeField] private bool deriveSamplesFromBounds = true;

        [Tooltip("Manual fallback when the above is off, or when the object has no renderer: " +
                 "topmost sample height above this transform, in metres.")]
        [SerializeField] private float sampleHeight = 0.05f;

        [Tooltip("Fraction of the object's height to pull the top and bottom samples inward, so " +
                 "they test the body rather than grazing its exact silhouette edge.")]
        [Range(0f, 0.4f)]
        [SerializeField] private float edgeInset = 0.15f;

        private static readonly List<VisionTarget> active = new List<VisionTarget>();

        /// <summary>Everything currently needing a visibility check.</summary>
        public static IReadOnlyList<VisionTarget> Active => active;

        /// <summary>Current reveal state, written by LineOfSightController.</summary>
        public RevealTier Tier { get; private set; } = RevealTier.Hidden;

        /// <summary>World position when last seen at Faint or better. Seeds the drifting ghost.</summary>
        public Vector3 LastSeenPosition { get; private set; }

        /// <summary>Velocity when last seen, so the ghost can drift along the right heading.</summary>
        public Vector3 LastSeenVelocity { get; private set; }

        /// <summary>Seconds since this was last seen. Grows while hidden, resets to 0 while visible.</summary>
        public float SecondsSinceSeen { get; private set; } = float.MaxValue;

        /// <summary>True once it has been seen at least once - nothing should ghost an enemy never spotted.</summary>
        public bool HasEverBeenSeen { get; private set; }

        private Renderer[] renderers;
        private readonly List<Material> ownedMaterials = new List<Material>();
        private bool materialsPrepared;

        // Sample offsets in this object's local space, resolved once at Awake. Local rather than
        // world so they stay correct as the target moves and rotates.
        private readonly Vector3[] localSampleOffsets = new Vector3[3];

        private void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>();
            LastSeenPosition = transform.position;
            ResolveSampleOffsets();
        }

        /// <summary>
        /// Places the three samples across the object's real vertical extent: near the bottom, at
        /// the centre, near the top. Falls back to the manual height when there is no renderer to
        /// measure (or when explicitly disabled).
        /// </summary>
        private void ResolveSampleOffsets()
        {
            float bottom, top;

            if (deriveSamplesFromBounds && TryGetLocalVerticalExtent(out bottom, out top))
            {
                float span = top - bottom;
                float inset = span * edgeInset;
                bottom += inset;
                top -= inset;
            }
            else
            {
                // Manual mode treats the transform origin as the base of the object.
                bottom = sampleHeight * 0.1f;
                top = sampleHeight;
            }

            localSampleOffsets[0] = new Vector3(0f, bottom, 0f);
            localSampleOffsets[1] = new Vector3(0f, (bottom + top) * 0.5f, 0f);
            localSampleOffsets[2] = new Vector3(0f, top, 0f);
        }

        private bool TryGetLocalVerticalExtent(out float bottom, out float top)
        {
            bottom = 0f;
            top = 0f;
            if (renderers == null || renderers.Length == 0) return false;

            bool any = false;
            Bounds combined = default;

            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                if (!any) { combined = renderer.bounds; any = true; }
                else combined.Encapsulate(renderer.bounds);
            }

            if (!any) return false;

            // Convert the world-space vertical extent into local space through the transform, so a
            // scaled prefab (these are ~0.05 scale) reports offsets that survive TransformPoint.
            Vector3 centre = combined.center;
            bottom = transform.InverseTransformPoint(new Vector3(centre.x, combined.min.y, centre.z)).y;
            top = transform.InverseTransformPoint(new Vector3(centre.x, combined.max.y, centre.z)).y;

            if (top < bottom) { float swap = bottom; bottom = top; top = swap; }
            return top - bottom > 1e-5f;
        }

        private void OnEnable() => active.Add(this);

        private void OnDisable() => active.Remove(this);

        private void OnDestroy()
        {
            // Material instances created here are not owned by any asset, so they leak unless
            // explicitly destroyed when the target dies.
            foreach (var material in ownedMaterials)
                if (material != null) Destroy(material);

            ownedMaterials.Clear();
        }

        /// <summary>The three points LineOfSightController raycasts to. Bottom, middle, top.</summary>
        public void GetSamplePoints(Vector3[] buffer)
        {
            if (buffer == null || buffer.Length < 3) return;

            for (int i = 0; i < 3; i++)
                buffer[i] = transform.TransformPoint(localSampleOffsets[i]);
        }

        /// <summary>Called by LineOfSightController each evaluation tick.</summary>
        public void ApplyTier(RevealTier tier, Vector3 currentVelocity, float deltaTime)
        {
            Tier = tier;

            if (tier == RevealTier.Hidden)
            {
                SecondsSinceSeen = HasEverBeenSeen
                    ? SecondsSinceSeen + deltaTime
                    : float.MaxValue;
            }
            else
            {
                HasEverBeenSeen = true;
                SecondsSinceSeen = 0f;
                LastSeenPosition = transform.position;
                LastSeenVelocity = currentVelocity;
            }

            ApplyAlpha(VisionMath.AlphaForTier(tier));
        }

        private void ApplyAlpha(float alpha)
        {
            if (renderers == null) return;

            PrepareMaterials();

            bool visible = alpha > 0.001f;
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;

                // Disabling outright at zero alpha is both cheaper than drawing a fully
                // transparent object and immune to any shader that ignores alpha.
                renderer.enabled = visible;
                if (visible) MaterialFx.SetAlpha(renderer.material, alpha);
            }
        }

        /// <summary>
        /// Converts this target's materials to transparent once, lazily. Done on first fade
        /// rather than in Awake so a target that is always fully visible never pays the cost of
        /// leaving the opaque render queue.
        /// </summary>
        private void PrepareMaterials()
        {
            if (materialsPrepared) return;
            materialsPrepared = true;

            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;

                // Touching .material instantiates a per-renderer copy, which is what we want -
                // fading one sentry must not fade every other object sharing that material.
                Material instance = renderer.material;
                MaterialFx.MakeTransparent(instance);
                ownedMaterials.Add(instance);
            }
        }
    }
}
