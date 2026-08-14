using System.Collections.Generic;
using UnityEngine;
using ScrapSiege.Core;
using ScrapSiege.Vision;

namespace ScrapSiege.Siege
{
    /// <summary>
    /// Draws a sentry actually shooting: a tracer from the sentry to each unit it damages, and a
    /// brief flash on that unit.
    ///
    /// <para><b>Why this exists.</b> <see cref="GarrisonSentry"/> deals its damage silently on an
    /// <c>InvokeRepeating</c> tick, so on device a unit simply lost health and eventually died with
    /// nothing on screen connecting the two. The player could not tell that a sentry was firing,
    /// which sentry was firing, or which of their units was being hit - so the cover mechanic the
    /// whole route choice hangs on was invisible, and dying units read as the disappearing-unit bug
    /// this project has already chased once.</para>
    ///
    /// <para><b>The visual is driven by the damage tick itself</b> - <see cref="ReportHit"/> is
    /// called from the same loop that applies the damage, rather than re-deriving who is in range.
    /// A range indicator that recomputes its own answer can disagree with the rule (exactly what
    /// happened with SentryArcVisualizer drawing at 4% of the real radius). Here the tracer cannot
    /// lie: if there is no tracer, no damage was dealt.</para>
    ///
    /// <para>Optional component. A sentry without one damages exactly as before, just silently.</para>
    /// </summary>
    [RequireComponent(typeof(GarrisonSentry))]
    public class SentryFireVisualizer : MonoBehaviour
    {
        [Tooltip("Any material using the active render pipeline - instanced and forced transparent. " +
                 "Assigned rather than Shader.Find'd because runtime shader lookup is unreliable in " +
                 "built players (see CLAUDE.md gotchas).")]
        [SerializeField] private Material tracerBaseMaterial;

        [SerializeField] private Color tracerColor = new Color(1f, 0.45f, 0.2f);

        [Tooltip("Seconds a tracer stays visible. Must be shorter than the sentry's tick interval or " +
                 "tracers overlap and the fire rate becomes unreadable.")]
        [SerializeField] private float tracerSeconds = 0.18f;

        [Tooltip("Tracer thickness as a fraction of board length, so it stays legible on any table size.")]
        [SerializeField] private float widthFraction = 0.004f;

        [Tooltip("How high up the target to aim, as a fraction of the target's own height. Firing at " +
                 "the transform origin would draw every tracer into the table surface.")]
        [Range(0f, 1f)]
        [SerializeField] private float targetHeightFraction = 0.6f;

        [Header("Hit flash on the struck unit")]
        [SerializeField] private Color flashColor = new Color(1f, 0.85f, 0.5f);
        [SerializeField] private float flashSeconds = 0.15f;

        private readonly List<Flash> flashes = new List<Flash>();

        private LineRenderer tracer;
        private float tracerRemaining;
        private float muzzleHeight;

        private struct Flash
        {
            public Renderer Renderer;
            public Material Material;
            public Color Original;
            public float Remaining;
        }

        private void Awake()
        {
            // Sentries are scaled up by WorldScale in GarrisonSentry.Awake, so a muzzle height in
            // real metres has to convert like every other authored distance.
            muzzleHeight = WorldScale.Metres(0.03f);
            BuildTracer();
        }

        private void BuildTracer()
        {
            if (tracerBaseMaterial == null)
            {
                Debug.LogWarning("SentryFireVisualizer: Tracer Base Material is not assigned - sentry fire will be " +
                                 "invisible, so the player cannot tell which unit is being shot.", this);
                return;
            }

            var go = new GameObject("SentryTracer");

            // Deliberately NOT parented to the sentry. The sentry prefab sits at roughly 0.04
            // localScale, and a LineRenderer in world space under a scaled parent draws its width
            // scaled too - the same trap that rendered SentryArcVisualizer's wedge at 4% of its real
            // size. An unparented object has no scale to fight.
            go.transform.SetParent(null);

            tracer = go.AddComponent<LineRenderer>();
            tracer.useWorldSpace = true;
            tracer.positionCount = 2;
            tracer.numCapVertices = 2;
            tracer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tracer.receiveShadows = false;

            var material = new Material(tracerBaseMaterial) { color = tracerColor };
            MaterialFx.MakeTransparent(material);
            tracer.material = material;

            tracer.enabled = false;
        }

        private void OnDestroy()
        {
            if (tracer != null) Destroy(tracer.gameObject);

            // Restore anything still mid-flash, then release the private copies we made.
            foreach (var flash in flashes)
            {
                if (flash.Renderer != null && flash.Material != null) flash.Material.color = flash.Original;
                if (flash.Material != null) Destroy(flash.Material);
            }
            flashes.Clear();
        }

        /// <summary>
        /// Called by <see cref="GarrisonSentry"/> on the tick it damages <paramref name="target"/>.
        /// </summary>
        public void ReportHit(SiegeUnit target, float boardLength)
        {
            if (target == null) return;

            ScrapSiege.Audio.GameAudio.Play(ScrapSiege.Audio.Sfx.SentryFire, 0.7f);

            DrawTracer(target, boardLength);
            FlashTarget(target);

            // Same burst every other damage source now draws, so "something is hitting that unit"
            // looks identical whether it came from a sentry, a marksman or a melee blow. A tracer
            // alone told the player where the shot came FROM but never quite that it connected.
            CombatFx.Impact(AimPoint(target), tracerColor, boardLength);
        }

        private void DrawTracer(SiegeUnit target, float boardLength)
        {
            if (tracer == null) return;

            // Read off the sentry rather than re-derived, so the tracer starts exactly where the
            // line-of-sight test says the sentry is watching from - the top of the chokepoint it
            // garrisons, not the ground beside it. A visual that computes its own answer can
            // disagree with the rule; that is how SentryArcVisualizer once drew at 4% of its range.
            var sentry = GetComponent<GarrisonSentry>();
            Vector3 muzzle = sentry != null
                ? sentry.EyePoint
                : transform.position + Vector3.up * muzzleHeight;

            Vector3 impact = AimPoint(target);

            tracer.SetPosition(0, muzzle);
            tracer.SetPosition(1, impact);

            float width = boardLength > 0f
                ? widthFraction * boardLength
                : WorldScale.Metres(0.002f);

            tracer.startWidth = width;
            tracer.endWidth = width * 0.4f;

            tracer.enabled = true;
            tracerRemaining = tracerSeconds;
        }

        /// <summary>
        /// Aims at the target's visual centre of mass rather than its transform origin, which sits on
        /// the table - a tracer to the origin draws along the floor and reads as a floor decal
        /// instead of a shot.
        /// </summary>
        private Vector3 AimPoint(SiegeUnit target)
        {
            var renderers = target.GetComponentsInChildren<Renderer>();
            bool any = false;
            Bounds bounds = default;

            foreach (var renderer in renderers)
            {
                if (renderer == null || !renderer.enabled) continue;
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }

            if (!any) return target.transform.position + Vector3.up * muzzleHeight;

            return new Vector3(
                bounds.center.x,
                Mathf.Lerp(bounds.min.y, bounds.max.y, targetHeightFraction),
                bounds.center.z);
        }

        private void FlashTarget(SiegeUnit target)
        {
            var renderer = target.GetComponentInChildren<Renderer>();
            if (renderer == null) return;

            // Touching .material takes a private copy, so flashing one unit cannot flash every other
            // unit sharing the team-tint material.
            Material instance = renderer.material;
            if (instance == null) return;
            if (!instance.HasProperty("_BaseColor") && !instance.HasProperty("_Color")) return;

            flashes.Add(new Flash
            {
                Renderer = renderer,
                Material = instance,
                Original = instance.color,
                Remaining = flashSeconds,
            });

            instance.color = flashColor;
        }

        private void Update()
        {
            TickTracer();
            TickFlashes();
        }

        private void TickTracer()
        {
            if (tracer == null || !tracer.enabled) return;

            tracerRemaining -= Time.deltaTime;
            if (tracerRemaining <= 0f) tracer.enabled = false;
        }

        private void TickFlashes()
        {
            for (int i = flashes.Count - 1; i >= 0; i--)
            {
                Flash flash = flashes[i];

                // The unit died mid-flash and took its renderer with it - drop the entry rather than
                // writing to a destroyed object.
                if (flash.Renderer == null || flash.Material == null)
                {
                    if (flash.Material != null) Destroy(flash.Material);
                    flashes.RemoveAt(i);
                    continue;
                }

                flash.Remaining -= Time.deltaTime;
                if (flash.Remaining > 0f)
                {
                    flashes[i] = flash;
                    continue;
                }

                flash.Material.color = flash.Original;
                flashes.RemoveAt(i);
            }
        }
    }
}
