using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using ScrapSiege.Core;

namespace ScrapSiege.Vision
{
    /// <summary>
    /// Mechanic 2 (plan.md Section 4): enemies are revealed only when genuinely visible from the
    /// player's real camera position, blocked by Wall/Spire/Watchtower terrain. Peeking round a
    /// virtual wall therefore means physically leaning - the thing a flat-screen game cannot copy.
    ///
    /// Three raycasts per target per tick against the terrain-occluder layer only. Deterministic,
    /// no ML, and cheap enough at tabletop scale (a handful of targets) to run at 15 Hz without
    /// touching the frame budget.
    ///
    /// Runs on a fixed tick rather than every frame because the reveal state does not need to
    /// change faster than a human can lean, and a steady tick keeps the ghost drift timing honest.
    /// </summary>
    public class LineOfSightController : MonoBehaviour
    {
        [SerializeField] private Camera arCamera;

        [Tooltip("Which layers block sight. Should be the terrain-occluder layer only - including " +
                 "the ground quad would block every ray, and including units would let enemies hide " +
                 "behind each other.")]
        [SerializeField] private LayerMask occluderMask = 1 << SiegeLayers.TerrainOccluder;

        [Tooltip("Evaluations per second. Vision does not need to update faster than a person can lean.")]
        [SerializeField] private float evaluationsPerSecond = 15f;

        [Tooltip("Ignore occluders closer than this to the camera - stops the player's own held " +
                 "position clipping a wall and blacking out the whole board.")]
        [SerializeField] private float nearClip = 0.02f;

        [Header("Last-known-position ghosts")]
        [SerializeField] private bool showGhosts = true;

        [Tooltip("Seconds a ghost keeps drifting before it stops moving. Beyond this it only fades.")]
        [SerializeField] private float maxGhostDriftSeconds = 3f;

        [Tooltip("Seconds until a ghost has fully faded away.")]
        [SerializeField] private float ghostLifetimeSeconds = 6f;

        [SerializeField] private float ghostPeakAlpha = 0.45f;
        [SerializeField] private float ghostSize = 0.05f;

        [Tooltip("Any opaque material using the active render pipeline - ghosts are instanced from " +
                 "this and forced transparent. Same pattern as TerrainObjectSpawner.baseMaterial, " +
                 "which exists because runtime Shader.Find is unreliable in built players.")]
        [SerializeField] private Material ghostBaseMaterial;

        [SerializeField] private Color ghostColor = new Color(0.95f, 0.35f, 0.35f);

        private readonly Vector3[] samplePoints = new Vector3[3];
        private readonly Dictionary<VisionTarget, GameObject> ghosts = new Dictionary<VisionTarget, GameObject>();
        private readonly List<VisionTarget> staleGhostKeys = new List<VisionTarget>();

        private float tickTimer;
        private float lastTickTime;

        private void Awake()
        {
            if (arCamera == null)
            {
                arCamera = Camera.main;
                if (arCamera == null)
                    Debug.LogError("LineOfSightController: AR Camera is not assigned and no MainCamera exists - line of sight cannot be evaluated and every enemy will stay hidden.", this);
            }

            if (occluderMask.value == 0)
                Debug.LogWarning("LineOfSightController: Occluder Mask is empty - nothing will ever block sight, so every enemy is permanently fully visible.", this);

            lastTickTime = Time.time;
        }

        private void Update()
        {
            if (arCamera == null) return;

            float interval = evaluationsPerSecond > 0f ? 1f / evaluationsPerSecond : 0f;
            tickTimer += Time.deltaTime;
            if (tickTimer < interval) return;

            float delta = Time.time - lastTickTime;
            lastTickTime = Time.time;
            tickTimer = 0f;

            Evaluate(delta);
        }

        private void Evaluate(float deltaTime)
        {
            Vector3 eye = arCamera.transform.position;

            foreach (var target in VisionTarget.Active)
            {
                if (target == null) continue;

                target.GetSamplePoints(samplePoints);

                int visible = 0;
                for (int i = 0; i < samplePoints.Length; i++)
                    if (HasClearLine(eye, samplePoints[i])) visible++;

                RevealTier tier = VisionMath.TierFromVisiblePoints(visible, samplePoints.Length);
                target.ApplyTier(tier, VelocityOf(target), deltaTime);

                UpdateGhost(target);
            }

            CleanUpOrphanedGhosts();
        }

        private bool HasClearLine(Vector3 eye, Vector3 point) => HasClearLine(eye, point, occluderMask, nearClip);

        /// <summary>
        /// True if nothing on the occluder layer sits between the eye and this point. Uses a
        /// distance-limited raycast rather than Linecast so the near-clip skip is expressible.
        ///
        /// Public and static so the sight rule can be exercised directly against a synthetic
        /// scene - this is the part of the mechanic that fails *silently* if the occluder layer
        /// or mask is wrong (everything simply stays visible), which is exactly the class of bug
        /// this project keeps paying for.
        /// </summary>
        public static bool HasClearLine(Vector3 eye, Vector3 point, LayerMask occluderMask, float nearClip)
        {
            Vector3 delta = point - eye;
            float distance = delta.magnitude;
            if (distance <= nearClip) return true;

            Vector3 direction = delta / distance;
            return !Physics.Raycast(
                eye + direction * nearClip,
                direction,
                distance - nearClip,
                occluderMask,
                QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// Velocity is only meaningful for targets that move. Static sentries return zero, which
        /// makes their ghost sit still and simply fade - correct, since a sentry that was at a
        /// chokepoint is still at that chokepoint.
        /// </summary>
        private static Vector3 VelocityOf(VisionTarget target)
        {
            var agent = target.GetComponent<NavMeshAgent>();
            return agent != null && agent.isOnNavMesh ? agent.velocity : Vector3.zero;
        }

        private void UpdateGhost(VisionTarget target)
        {
            if (!showGhosts) return;

            bool shouldShow = target.Tier == RevealTier.Hidden
                              && target.HasEverBeenSeen
                              && target.SecondsSinceSeen < ghostLifetimeSeconds;

            if (!shouldShow)
            {
                if (ghosts.TryGetValue(target, out var existing) && existing != null)
                    existing.SetActive(false);
                return;
            }

            if (!ghosts.TryGetValue(target, out var ghost) || ghost == null)
            {
                ghost = CreateGhost();
                if (ghost == null) return;
                ghosts[target] = ghost;
            }

            ghost.SetActive(true);
            ghost.transform.position = VisionMath.DriftedGhostPosition(
                target.LastSeenPosition,
                target.LastSeenVelocity,
                target.SecondsSinceSeen,
                maxGhostDriftSeconds);

            float alpha = VisionMath.GhostAlpha(target.SecondsSinceSeen, ghostLifetimeSeconds, ghostPeakAlpha);
            var renderer = ghost.GetComponent<Renderer>();
            if (renderer != null) MaterialFx.SetAlpha(renderer.material, alpha);
        }

        private GameObject CreateGhost()
        {
            if (ghostBaseMaterial == null)
            {
                Debug.LogWarning("LineOfSightController: Ghost Base Material is not assigned - last-known-position ghosts are disabled. Assign any URP material to enable them.", this);
                showGhosts = false;
                return null;
            }

            var ghost = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ghost.name = "LastKnownGhost";

            // A marker must never block sight, absorb a deploy tap, or carve the NavMesh.
            var collider = ghost.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            ghost.transform.localScale = Vector3.one * ghostSize;

            var renderer = ghost.GetComponent<Renderer>();
            var material = new Material(ghostBaseMaterial) { color = ghostColor };
            MaterialFx.MakeTransparent(material);
            renderer.material = material;

            return ghost;
        }

        /// <summary>Drops ghosts whose target has been destroyed, so the dictionary can't grow unbounded.</summary>
        private void CleanUpOrphanedGhosts()
        {
            staleGhostKeys.Clear();

            foreach (var pair in ghosts)
                if (pair.Key == null) staleGhostKeys.Add(pair.Key);

            foreach (var key in staleGhostKeys)
            {
                if (ghosts.TryGetValue(key, out var ghost) && ghost != null)
                    Destroy(ghost);

                ghosts.Remove(key);
            }
        }
    }
}
