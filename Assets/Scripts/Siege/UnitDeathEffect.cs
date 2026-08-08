using System.Collections.Generic;
using UnityEngine;
using ScrapSiege.Core;
using ScrapSiege.Vision;

namespace ScrapSiege.Siege
{
    /// <summary>
    /// Breaks a dead unit into its own body parts, throws them, drops them onto the table and fades
    /// them out. Replaces the bare <c>Destroy(gameObject)</c> a killed unit used to get.
    ///
    /// <para><b>Why this is not cosmetic.</b> A unit that vanishes on the frame it dies is
    /// indistinguishable from a unit that vanished because of a bug - which this project has already
    /// spent a session chasing (the UnitAnimator local-space bob that flung troopers out of frame).
    /// A visible death is the difference between "he was killed" and "the game broke".</para>
    ///
    /// <para><b>Why the unit's own parts and not generic cubes.</b> The trooper is already a dozen
    /// separate meshes with real pivots, so reusing them is free, needs no new art, reads as the
    /// figure genuinely coming apart rather than as a puff of debris, and keeps the
    /// <see cref="UnitTeamTint"/> colours so you can still tell whose unit just died.</para>
    ///
    /// <para><b>Why no Rigidbodies.</b> The AR world is scaled up by <see cref="WorldScale.Scale"/>,
    /// and Unity's gravity is a fixed 9.81 in <i>Unity</i> metres. At a scale of 5 that reads as
    /// 9.81/5 = 1.96 real m/s² - lunar, and the debris drifts down like confetti. Rather than fight
    /// <c>Physics.gravity</c> globally (which every other system would then inherit), motion is
    /// integrated by hand against a scaled constant. That also means no colliders, no interaction
    /// with the NavMesh carving, no solver cost with several units dying at once, and identical
    /// behaviour every run.</para>
    /// </summary>
    public class UnitDeathEffect : MonoBehaviour
    {
        /// <summary>Real-world gravity. Scaled into the AR world at use, never authored pre-scaled.</summary>
        private const float RealGravity = 9.81f;

        private struct Piece
        {
            public Transform Transform;
            public Vector3 Velocity;
            public Vector3 SpinAxis;
            public float SpinSpeed;
            public bool Resting;
        }

        private readonly List<Piece> pieces = new List<Piece>();
        private readonly List<Material> ownedMaterials = new List<Material>();

        private float lifetimeSeconds = 2f;
        private float fadeStartFraction = 0.55f;
        private float elapsed;
        private float groundY;

        /// <summary>
        /// Detaches <paramref name="unit"/>'s renderer parts into a standalone debris object and
        /// starts the effect. The caller still destroys the unit itself - this deliberately does not,
        /// because the unit's own death bookkeeping (deregistering from <see cref="SiegeUnit.Active"/>,
        /// releasing any duel lock) has to happen on the caller's terms, not ours.
        /// </summary>
        /// <param name="unit">The dying unit. Its renderer children are moved out of it.</param>
        /// <param name="groundHeight">World Y of the table surface, so pieces settle on it rather than falling forever.</param>
        /// <param name="lifetime">Seconds until the debris is gone entirely.</param>
        public static void Play(GameObject unit, float groundHeight, float lifetime = 2f)
        {
            if (unit == null) return;

            var renderers = unit.GetComponentsInChildren<Renderer>();
            if (renderers == null || renderers.Length == 0) return;

            var root = new GameObject($"Debris_{unit.name}");
            root.transform.position = unit.transform.position;

            var effect = root.AddComponent<UnitDeathEffect>();
            effect.lifetimeSeconds = Mathf.Max(0.1f, lifetime);
            effect.groundY = groundHeight;
            effect.Capture(unit, renderers);

            // Nothing left to animate (every renderer was disabled, e.g. the sentry prefab's
            // vestigial sphere) - drop the empty object rather than leaving a no-op behaviour.
            if (effect.pieces.Count == 0) Destroy(root);
        }

        /// <summary>
        /// Moves each visible part onto the debris root and gives it a launch velocity.
        ///
        /// Speeds are derived from the unit's own measured height rather than authored in metres,
        /// for exactly the reason UnitAnimator resolves its bob and lunge the same way: these parts
        /// live in an imported FBX's local space, whose scale has already changed by ~54x once in
        /// this project's history. Expressed in body-heights per second, the effect survives the
        /// next re-export.
        /// </summary>
        private void Capture(GameObject unit, Renderer[] renderers)
        {
            float bodyHeight = MeasureHeight(renderers);
            if (bodyHeight <= 1e-5f) bodyHeight = WorldScale.Metres(0.05f);

            Vector3 centre = unit.transform.position + Vector3.up * bodyHeight * 0.5f;

            foreach (var renderer in renderers)
            {
                if (renderer == null || !renderer.enabled) continue;

                Transform part = renderer.transform;

                // Never steal the unit's own root: it carries the NavMeshAgent and SiegeUnit, and
                // reparenting it would drag the live component graph into the debris.
                if (part == unit.transform) continue;

                part.SetParent(transform, worldPositionStays: true);

                // Outward from the body's centre, so the figure bursts apart instead of every piece
                // drifting the same way. Upward bias sells the "knocked off its feet" read.
                Vector3 outward = part.position - centre;
                if (outward.sqrMagnitude < 1e-8f) outward = Random.insideUnitSphere;
                outward.Normalize();

                Vector3 launch = (outward + Vector3.up * 1.4f).normalized
                                 * bodyHeight * Random.Range(1.8f, 3.4f);

                pieces.Add(new Piece
                {
                    Transform = part,
                    Velocity = launch,
                    SpinAxis = Random.onUnitSphere,
                    SpinSpeed = Random.Range(180f, 540f),
                    Resting = false,
                });

                PrepareMaterials(renderer);
            }
        }

        private static float MeasureHeight(Renderer[] renderers)
        {
            bool any = false;
            Bounds bounds = default;

            foreach (var renderer in renderers)
            {
                if (renderer == null || !renderer.enabled) continue;
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }

            return any ? bounds.size.y : 0f;
        }

        /// <summary>
        /// Takes a private copy of each part's material so fading the debris cannot fade the living
        /// units that share the team-tint material. The copies are owned here and destroyed with the
        /// effect - the same ownership rule <see cref="VisionTarget"/> follows.
        /// </summary>
        private void PrepareMaterials(Renderer renderer)
        {
            var instances = renderer.materials;
            foreach (var instance in instances)
            {
                if (instance == null) continue;
                MaterialFx.MakeTransparent(instance);
                ownedMaterials.Add(instance);
            }
        }

        private void Update()
        {
            elapsed += Time.deltaTime;

            Integrate(Time.deltaTime);
            Fade();

            if (elapsed >= lifetimeSeconds) Destroy(gameObject);
        }

        private void Integrate(float deltaTime)
        {
            float gravity = WorldScale.Metres(RealGravity);

            for (int i = 0; i < pieces.Count; i++)
            {
                Piece piece = pieces[i];
                if (piece.Transform == null || piece.Resting) continue;

                piece.Velocity += Vector3.down * gravity * deltaTime;
                Vector3 next = piece.Transform.position + piece.Velocity * deltaTime;

                if (next.y <= groundY)
                {
                    // Settle rather than bounce. A bounce at this scale is a single-frame twitch that
                    // reads as a glitch, and a piece still rolling while it fades looks unfinished.
                    next.y = groundY;
                    piece.Resting = true;
                }

                piece.Transform.position = next;
                piece.Transform.Rotate(piece.SpinAxis, piece.SpinSpeed * deltaTime, Space.World);

                pieces[i] = piece;
            }
        }

        /// <summary>
        /// Holds full opacity for the first part of the life, then fades. Fading from the first
        /// frame makes a death read as "the unit was never really there"; holding briefly makes it
        /// read as a kill the player can actually notice and attribute.
        /// </summary>
        private void Fade()
        {
            float normalized = Mathf.Clamp01(elapsed / lifetimeSeconds);
            if (normalized < fadeStartFraction) return;

            float fade = Mathf.InverseLerp(fadeStartFraction, 1f, normalized);
            float alpha = 1f - fade;

            foreach (var material in ownedMaterials)
                if (material != null) MaterialFx.SetAlpha(material, alpha);
        }

        private void OnDestroy()
        {
            foreach (var material in ownedMaterials)
                if (material != null) Destroy(material);

            ownedMaterials.Clear();
        }
    }
}
