using System.Collections.Generic;
using UnityEngine;
using ScrapSiege.Core;
using ScrapSiege.Vision;

namespace ScrapSiege.Siege
{
    /// <summary>
    /// A shared, pooled tracer service for unit-vs-unit shots.
    ///
    /// <para><b>Why not the SentryFireVisualizer pattern.</b> That component owns one LineRenderer
    /// per sentry, which is fine because a level has one to three sentries that live for the whole
    /// match. Units are spawned and destroyed constantly and there can be a dozen alive at once, so
    /// per-unit LineRenderers would mean a GameObject churn on every deploy and death. One pool of
    /// unparented lines, reused round-robin, costs a fixed allocation up front instead.</para>
    ///
    /// <para>Unparented for the same reason SentryFireVisualizer is: a LineRenderer under a ~0.04
    /// localScale parent draws its width scaled too, which is the bug that once rendered the sentry
    /// arc at 4% of its real size.</para>
    ///
    /// <para>Optional. With no instance in the scene, ranged units still deal damage - the shot is
    /// simply invisible, and a single warning says so once rather than every frame.</para>
    /// </summary>
    public class CombatFx : MonoBehaviour
    {
        [Tooltip("Any material using the active render pipeline - tracers are instanced from it and " +
                 "forced transparent. Assigned rather than Shader.Find'd, which is unreliable in " +
                 "built players (CLAUDE.md gotchas).")]
        [SerializeField] private Material tracerBaseMaterial;

        [Tooltip("How many shots can be in flight at once. A busy board fires perhaps three or four " +
                 "per tick; beyond the pool size the oldest tracer is reused, which reads fine.")]
        [SerializeField] private int poolSize = 10;

        [Tooltip("Seconds a tracer stays visible. Short enough that a 0.5s attack tick reads as " +
                 "discrete shots rather than a continuous beam.")]
        [SerializeField] private float tracerSeconds = 0.12f;

        [Tooltip("Tracer thickness as a fraction of board length, so it stays legible on any table.")]
        [SerializeField] private float widthFraction = 0.0035f;

        [Header("Impact bursts")]
        [Tooltip("How many bursts can overlap. A busy melee produces one per blow per fight; beyond " +
                 "the pool the oldest is recycled, which reads fine at this speed.")]
        [SerializeField] private int burstPoolSize = 12;

        [Tooltip("Shards per burst. Four to six reads as an impact; more reads as an explosion, which " +
                 "is the wrong scale for one hit between two 5cm figures.")]
        [SerializeField] private int shardsPerBurst = 5;

        [Tooltip("Seconds a burst lives. Must stay well under the 0.5s attack tick or consecutive " +
                 "blows smear into a continuous glow and the rhythm of a fight disappears.")]
        [SerializeField] private float burstSeconds = 0.22f;

        [Tooltip("Shard size as a fraction of board length - board-relative like everything else, so " +
                 "an impact is the same size relative to the fight on any table.")]
        [SerializeField] private float burstShardFraction = 0.006f;

        [Tooltip("How far shards travel, as a fraction of board length.")]
        [SerializeField] private float burstSpreadFraction = 0.012f;

        private static CombatFx instance;
        private static bool warnedMissing;

        private readonly List<LineRenderer> pool = new List<LineRenderer>();
        private readonly List<float> remaining = new List<float>();
        private int nextTracer;

        private readonly List<Burst> bursts = new List<Burst>();
        private int nextBurst;

        /// <summary>
        /// One impact: a handful of shards thrown out from the contact point, fading as they go.
        ///
        /// <para>Deliberately a plain struct of transforms rather than a ParticleSystem. The shards
        /// have to be sized from board length (a particle system's sizes are authored absolutely, and
        /// this project has repeatedly been burned by absolute sizes surviving into a rescaled
        /// world), the whole AR world runs at <see cref="WorldScale.Scale"/>, and a pooled handful of
        /// cubes costs less than a dozen particle systems on a mid-range phone.</para>
        /// </summary>
        private struct Burst
        {
            public Transform Root;
            public Transform[] Shards;
            public Vector3[] Directions;
            public Material[] Materials;
            public float Remaining;
            public float Lifetime;
            public float Spread;
        }

        private void Awake()
        {
            // Last one in wins rather than destroying itself: the match scene has exactly one of
            // these, and a duplicate is an authoring mistake worth surviving, not crashing on.
            instance = this;

            if (tracerBaseMaterial == null)
            {
                Debug.LogWarning("CombatFx: Tracer Base Material is not assigned - ranged units will " +
                                 "deal damage with nothing visible connecting shooter and target.", this);
                return;
            }

            for (int i = 0; i < Mathf.Max(1, poolSize); i++)
            {
                var go = new GameObject($"CombatTracer_{i}");
                go.transform.SetParent(null);

                var line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.numCapVertices = 2;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;

                var material = new Material(tracerBaseMaterial);
                MaterialFx.MakeTransparent(material);
                line.material = material;
                line.enabled = false;

                pool.Add(line);
                remaining.Add(0f);
            }

            BuildBursts();
        }

        private void BuildBursts()
        {
            int shards = Mathf.Max(1, shardsPerBurst);

            for (int i = 0; i < Mathf.Max(1, burstPoolSize); i++)
            {
                var root = new GameObject($"CombatBurst_{i}");
                root.transform.SetParent(null);

                var burst = new Burst
                {
                    Root = root.transform,
                    Shards = new Transform[shards],
                    Directions = new Vector3[shards],
                    Materials = new Material[shards],
                };

                for (int s = 0; s < shards; s++)
                {
                    var shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    shard.name = "Shard";

                    // Same rule as every other cosmetic here: never block sight, carve the NavMesh,
                    // absorb a deploy tap or nudge an agent.
                    var collider = shard.GetComponent<Collider>();
                    if (collider != null) Destroy(collider);

                    shard.transform.SetParent(root.transform, worldPositionStays: false);

                    var renderer = shard.GetComponent<Renderer>();
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;

                    var material = new Material(tracerBaseMaterial);
                    MaterialFx.MakeTransparent(material);
                    renderer.material = material;

                    burst.Shards[s] = shard.transform;
                    burst.Materials[s] = material;
                }

                root.SetActive(false);
                bursts.Add(burst);
            }
        }

        private void OnDestroy()
        {
            foreach (var line in pool)
                if (line != null) Destroy(line.gameObject);

            foreach (var burst in bursts)
            {
                if (burst.Root != null) Destroy(burst.Root.gameObject);
                if (burst.Materials == null) continue;
                foreach (var material in burst.Materials)
                    if (material != null) Destroy(material);
            }

            pool.Clear();
            remaining.Clear();
            bursts.Clear();

            if (instance == this) instance = null;
        }

        /// <summary>
        /// Draws one shot. Safe to call with no CombatFx in the scene - warns once, then no-ops.
        /// </summary>
        public static void Shot(Vector3 from, Vector3 to, Color color, float boardLength)
        {
            if (instance == null)
            {
                if (!warnedMissing)
                {
                    warnedMissing = true;
                    Debug.LogWarning("CombatFx: no instance in the scene - ranged attacks will be invisible. " +
                                     "Add a CombatFx component (ARMechanics is the natural home) and assign its material.");
                }
                return;
            }

            instance.DrawShot(from, to, color, boardLength);
        }

        /// <summary>
        /// A hit landing. Call from whatever applies the damage, never from a separate "is something
        /// probably being hit" check - the same rule SentryFireVisualizer follows, so the effect
        /// physically cannot show a blow that dealt nothing.
        ///
        /// <para>This exists because <b>melee had no visual at all</b>. A ranged unit drew a tracer,
        /// a sentry drew a tracer and flashed its target, but two units meeting in the middle of the
        /// board just stood next to each other with a small lunge while numbers moved invisibly. On a
        /// phone held over a table that reads as two figures loitering, not as a fight - which is a
        /// real problem for a game whose whole appeal is watching the battle happen on your own
        /// furniture.</para>
        ///
        /// <param name="scale">Multiplier on the burst size. 1 is a unit-on-unit blow; the base
        /// impact uses a larger value so smashing a building does not look like a scuffle.</param>
        /// </summary>
        public static void Impact(Vector3 point, Color color, float boardLength, float scale = 1f)
        {
            if (instance == null) return;
            instance.PlayBurst(point, color, boardLength, scale);
        }

        private void PlayBurst(Vector3 point, Color color, float boardLength, float scale)
        {
            if (bursts.Count == 0) return;

            int index = nextBurst;
            nextBurst = (nextBurst + 1) % bursts.Count;

            Burst burst = bursts[index];
            if (burst.Root == null) return;

            float unit = boardLength > 0f ? boardLength : WorldScale.Metres(0.6f);
            float shardSize = burstShardFraction * unit * scale;
            float spread = burstSpreadFraction * unit * scale;

            burst.Root.position = point;
            burst.Root.gameObject.SetActive(true);

            for (int s = 0; s < burst.Shards.Length; s++)
            {
                // Biased upward so debris arcs off the table rather than spraying along it, where
                // it would be hidden by the very units that just collided.
                Vector3 direction = (Random.onUnitSphere + Vector3.up * 1.2f).normalized;
                burst.Directions[s] = direction;

                burst.Shards[s].localPosition = direction * shardSize * 0.35f;
                burst.Shards[s].localRotation = Random.rotation;
                burst.Shards[s].localScale = Vector3.one * shardSize * Random.Range(0.6f, 1.3f);

                if (burst.Materials[s] != null) burst.Materials[s].color = color;
            }

            burst.Remaining = burstSeconds;
            burst.Lifetime = burstSeconds;
            burst.Spread = spread;
            bursts[index] = burst;
        }

        private void DrawShot(Vector3 from, Vector3 to, Color color, float boardLength)
        {
            if (pool.Count == 0) return;

            int index = nextTracer;
            nextTracer = (nextTracer + 1) % pool.Count;

            var line = pool[index];
            if (line == null) return;

            float width = boardLength > 0f ? widthFraction * boardLength : WorldScale.Metres(0.002f);

            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.startWidth = width;
            line.endWidth = width * 0.35f;
            line.material.color = color;
            line.enabled = true;

            remaining[index] = tracerSeconds;
        }

        private void Update()
        {
            for (int i = 0; i < pool.Count; i++)
            {
                if (remaining[i] <= 0f) continue;

                remaining[i] -= Time.deltaTime;
                if (remaining[i] > 0f) continue;

                if (pool[i] != null) pool[i].enabled = false;
            }

            TickBursts();
        }

        private void TickBursts()
        {
            for (int i = 0; i < bursts.Count; i++)
            {
                Burst burst = bursts[i];
                if (burst.Remaining <= 0f || burst.Root == null) continue;

                burst.Remaining -= Time.deltaTime;
                if (burst.Remaining <= 0f)
                {
                    burst.Root.gameObject.SetActive(false);
                    bursts[i] = burst;
                    continue;
                }

                // Eased outward travel: fast at the moment of impact, slowing as it fades, which is
                // what makes a burst read as a hit rather than as a steady expanding bubble.
                float t = 1f - (burst.Remaining / Mathf.Max(burst.Lifetime, 1e-4f));
                float travel = burst.Spread * (1f - (1f - t) * (1f - t));
                float alpha = 1f - t;

                for (int s = 0; s < burst.Shards.Length; s++)
                {
                    if (burst.Shards[s] == null) continue;
                    burst.Shards[s].localPosition = burst.Directions[s] * travel;
                    burst.Shards[s].Rotate(burst.Directions[s], 480f * Time.deltaTime, Space.World);
                    if (burst.Materials[s] != null) MaterialFx.SetAlpha(burst.Materials[s], alpha);
                }

                bursts[i] = burst;
            }
        }
    }
}
