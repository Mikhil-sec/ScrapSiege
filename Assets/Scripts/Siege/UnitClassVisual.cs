using UnityEngine;
using ScrapSiege.Core;

namespace ScrapSiege.Siege
{
    /// <summary>
    /// Makes a unit's class readable at tabletop scale by bolting a crude primitive accessory onto
    /// the shared trooper model.
    ///
    /// <para><b>Why not five authored models.</b> Colour is already spent - the body carries the
    /// team colour, and that is the single most important thing to read on a crowded board
    /// (see <see cref="UnitTeamTint"/>). So class has to come from silhouette. Five Blender models
    /// would do it better, but each one is a round trip through the art pipeline, and adding a sixth
    /// class later would need another. Primitives generated here mean a new class is an asset and a
    /// roster entry, which is the same promise <see cref="ScrapSiege.Levels.LevelDefinition"/> makes
    /// for levels. Upgrading any individual silhouette to a real model later is a drop-in.</para>
    ///
    /// <para><b>Everything is sized from the unit's own measured bounds</b>, never from typed
    /// metres. This project has already paid twice for hand-typed sizes on this prefab - the
    /// UnitAnimator bob authored in metres against a model whose scale changed 54x, and the
    /// VisionTarget sample height that sampled empty air. Measuring means a re-export cannot
    /// silently produce a shield bigger than the soldier holding it.</para>
    ///
    /// <para>Runs after <see cref="UnitTeamTint"/> (which paints in Awake, whereas
    /// <see cref="Apply"/> is called at spawn), so accessories deliberately keep their own accent
    /// colour rather than being repainted into the team colour and vanishing.</para>
    /// </summary>
    public class UnitClassVisual : MonoBehaviour
    {
        private const string AccessoryName = "ClassSilhouette";

        private Material accessoryTemplate;

        /// <summary>
        /// Called by <see cref="SiegeUnit.ApplyClass"/>. Safe to call with a null or None silhouette.
        /// </summary>
        public void Apply(UnitClass definition)
        {
            if (definition == null) return;

            // A real authored model wins outright. The primitive accessories below were always a
            // stand-in for this - see the class comment - and bolting a code-built cube onto a model
            // that already has a rifle would just clutter the silhouette it was built to carry.
            GameObject prefab = ResolveModelPrefab(definition);
            if (prefab != null)
            {
                if (SwapInClassModel(definition, prefab)) return;

                Debug.LogWarning($"{name}: class '{definition.displayName}' has a Model Prefab but it " +
                                 "could not be swapped in - falling back to the primitive silhouette.", this);
            }

            if (definition.silhouette == ClassSilhouette.None) return;

            Bounds local = MeasureLocalBounds();
            if (local.size.y <= 0f) return;

            // The template is stolen from whatever the unit is already wearing, so the accessory
            // always uses the active render pipeline's shader without needing a serialized material
            // on the prefab. Runtime Shader.Find is unreliable in built players (CLAUDE.md gotchas)
            // and a new serialized field is one more thing to forget to assign.
            accessoryTemplate = FindTemplateMaterial();
            if (accessoryTemplate == null) return;

            if (definition.silhouette == ClassSilhouette.Turret)
                BuildTurret(local, definition.accentColor);
            else
                BuildAccessory(definition.silhouette, local, definition.accentColor);

            // The primitive path changes which renderers exist too - it adds accessories, and the
            // Turret silhouette disables the whole figure. Same rule as the model-swap path above:
            // anything caching renderers at Awake has to be told.
            var vision = GetComponent<ScrapSiege.Vision.VisionTarget>();
            if (vision != null) vision.RefreshRenderers();
        }

        /// <summary>
        /// Replaces the shared trooper body with the class's own authored model.
        ///
        /// <para><b>Why a swap and not five prefabs.</b> A prefab per class would mean five copies of
        /// the NavMeshAgent settings, the collider, the four behaviour components and every tuning
        /// value on them - and this project has already been bitten twice by a serialized value
        /// drifting away from its source. Everything except the mesh is genuinely identical between
        /// classes, so only the mesh is swapped, and "ship a new class" stays an asset plus a roster
        /// entry (the promise <see cref="UnitRoster"/> and LevelDefinition both make).</para>
        ///
        /// <para><b>Height is normalised, not assumed.</b> The shared trooper FBX imports at 1/100
        /// scale with a -90 degrees X root rotation (its prefab compensates with a Visual child at
        /// scale 100), whereas the class models are exported with the project's documented settings
        /// and import at 1:1 with no root rotation. Matching those conventions by hand is exactly the
        /// class of thing that silently produces a speck or a giant, so the swapped model is measured
        /// and scaled to the height the body it replaced actually had. That also means an artist can
        /// re-export at any scale without touching code.</para>
        ///
        /// <para>Order matters on the way out: the tint has to be re-applied because
        /// <see cref="UnitTeamTint"/> painted at Awake, before this model existed, and the animator
        /// has to be re-bound because it cached <c>Torso</c>/<c>Leg_L</c>/<c>Leg_R</c> from the body
        /// that is now hidden.</para>
        /// </summary>
        /// <summary>
        /// Which model this unit wears: the class's own, or its Veteran re-skin while Pro is active.
        ///
        /// <para><b>Why a cosmetic tier matters here specifically.</b> The one Pro perk that touches
        /// gameplay is the Turret class, and that has always been the arguable call in this project
        /// (plan.md Section 10) - it is the only defensive class, on the levels where defence is
        /// what keeps you alive. A skin set is the opposite kind of perk: it is worth paying for, it
        /// is visible to other people watching you play, and it cannot possibly win a match. Adding
        /// one gives the subscription something to sell that is not power.</para>
        ///
        /// <para>Falls back to the base model whenever the entitlement is off OR no veteran model is
        /// authored, so a class without a skin is simply a class without a skin rather than an
        /// invisible unit - and so shipping the fifth skin later needs no code.</para>
        /// </summary>
        private static GameObject ResolveModelPrefab(UnitClass definition)
        {
            if (definition.proModelPrefab != null && ScrapSiege.Monetization.ProEntitlement.IsUnlocked)
                return definition.proModelPrefab;

            return definition.modelPrefab;
        }

        private bool SwapInClassModel(UnitClass definition, GameObject prefab)
        {
            float targetHeight = MeasureWorldHeight(gameObject);

            // Hidden rather than destroyed: UnitDeathEffect walks renderers and skips disabled ones,
            // and keeping the original around means a bad model asset degrades to an invisible unit
            // that can still be diagnosed rather than to an unrecoverable prefab.
            var originalRenderers = new System.Collections.Generic.List<Renderer>();
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled) continue;
                originalRenderers.Add(renderer);
            }

            GameObject model = Instantiate(prefab, transform);
            model.name = ClassModelName;
            model.transform.localPosition = Vector3.zero;

            // The prefab's OWN root rotation and scale are kept, never overwritten. A Blender FBX
            // arrives with a -90 degrees X axis correction and a unit-conversion scale baked onto its
            // root by the importer, and this project has already shipped the bug where code reset
            // that and laid every model on its back (CLAUDE.md, 2026-08-08). Whatever the importer
            // decided is correct by definition; the only thing this method is entitled to change is
            // the magnitude, and it does that as a multiplier below.
            Vector3 importScale = model.transform.localScale;

            float modelHeight = MeasureWorldHeight(model);
            if (modelHeight <= 1e-6f || targetHeight <= 1e-6f)
            {
                Destroy(model);
                return false;
            }

            model.transform.localScale = importScale * (targetHeight / modelHeight);

            // Sit the model's feet on the same plane the old body stood on. Authored models have a
            // hairline of geometry below zero from the base disc's bottom face; without this they
            // hover or sink by a fraction of a millimetre, which at 5cm is visible as a float.
            float footOffset = MeasureWorldMinY(model) - transform.position.y;
            model.transform.localPosition = new Vector3(0f, -footOffset / Mathf.Max(transform.lossyScale.y, 1e-6f), 0f);

            foreach (var renderer in originalRenderers)
                if (renderer != null) renderer.enabled = false;

            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                // A swapped-in model must be as inert as the primitives it replaces: no sight
                // blocking, no NavMesh carving, no eating a deploy tap.
                renderer.gameObject.layer = gameObject.layer;
            }
            foreach (var collider in model.GetComponentsInChildren<Collider>(true))
                Destroy(collider);

            // Everything below is a rebind hook, and the reason they all have to be here is one
            // rule worth stating plainly: ANY component that caches renderers or child transforms
            // at Awake is invalidated by this swap. Awake ran at Instantiate, before this model
            // existed and while the body it replaces was still the only thing on the unit.
            //
            // UnitTeamTint and UnitAnimator already had a hook. VisionTarget did NOT, and that
            // omission is the whole of the 2026-08-13 device report "the pro cosmetics look like
            // there are 2 models inside each other": it kept the ORIGINAL body's renderer array and
            // set renderer.enabled = true across it the first time the player laid eyes on the unit,
            // switching the hidden trooper - spear included - back on inside the class model.
            //
            // If a fourth such component is ever added, it needs a hook here too.

            var tint = GetComponent<UnitTeamTint>();
            if (tint != null) tint.Apply();

            var vision = GetComponent<ScrapSiege.Vision.VisionTarget>();
            if (vision != null) vision.RefreshRenderers();

            // Scoped to the new model, NOT re-run over the whole unit. Both of these look parts up
            // by name, the hidden body still owns those exact names, and it sits earlier in the
            // hierarchy - so an unscoped re-run finds the old body again and changes nothing. That
            // is precisely how the previous parameterless Rebind() failed silently.
            var animator = GetComponent<UnitAnimator>();
            if (animator != null) animator.Rebind(model.transform);

            var muzzle = GetComponent<UnitMuzzle>();
            if (muzzle != null) muzzle.Rebind(model.transform);

            return true;
        }

        private const string ClassModelName = "ClassModel";

        private static float MeasureWorldHeight(GameObject root)
        {
            if (!TryMeasure(root, out Bounds bounds)) return 0f;
            return bounds.size.y;
        }

        private static float MeasureWorldMinY(GameObject root)
        {
            if (!TryMeasure(root, out Bounds bounds)) return 0f;
            return bounds.min.y;
        }

        private static bool TryMeasure(GameObject root, out Bounds bounds)
        {
            bounds = default;
            bool any = false;

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || !renderer.enabled) continue;
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }

            return any;
        }

        private void BuildAccessory(ClassSilhouette silhouette, Bounds local, Color accent)
        {
            float h = local.size.y;

            Vector3 size;
            Vector3 position;

            switch (silhouette)
            {
                // A wide flat slab carried across the front. Doubles the unit's apparent width from
                // every angle, which is the read we want for the thing that soaks hits.
                case ClassSilhouette.Shield:
                    size = new Vector3(h * 0.85f, h * 0.70f, h * 0.10f);
                    position = new Vector3(0f, local.min.y + h * 0.42f, h * 0.30f);
                    break;

                // A long thin bar held out to one side. Length is the read - a marksman is the only
                // thing on the board wider than it is tall.
                case ClassSilhouette.Rifle:
                    size = new Vector3(h * 0.10f, h * 0.10f, h * 1.05f);
                    position = new Vector3(h * 0.22f, local.min.y + h * 0.62f, h * 0.28f);
                    break;

                // A short swept blade low on the body. Small and pointed, so a saboteur reads as
                // something that is not going to stand and fight.
                case ClassSilhouette.Blade:
                    size = new Vector3(h * 0.07f, h * 0.55f, h * 0.07f);
                    position = new Vector3(h * 0.26f, local.min.y + h * 0.55f, h * 0.06f);
                    break;

                default:
                    return;
            }

            var accessory = MakeBox(AccessoryName, size, position, accent);
            if (silhouette == ClassSilhouette.Blade)
                accessory.transform.localRotation = Quaternion.Euler(35f, 0f, 0f);
        }

        /// <summary>
        /// An emplacement is not a soldier, so the figure is hidden outright and replaced with a
        /// squat mount and barrel. Hiding rather than destroying keeps the model available if a
        /// future class ever wants to reuse the same prefab differently.
        /// </summary>
        private void BuildTurret(Bounds local, Color accent)
        {
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
                if (renderer.gameObject != gameObject) renderer.enabled = false;

            var rootRenderer = GetComponent<Renderer>();
            if (rootRenderer != null) rootRenderer.enabled = false;

            float h = local.size.y;
            float baseY = local.min.y;

            MakeBox("TurretBase", new Vector3(h * 0.75f, h * 0.30f, h * 0.75f),
                    new Vector3(0f, baseY + h * 0.15f, 0f), accent * 0.55f);

            MakeBox("TurretHousing", new Vector3(h * 0.48f, h * 0.34f, h * 0.48f),
                    new Vector3(0f, baseY + h * 0.47f, 0f), accent);

            MakeBox("TurretBarrel", new Vector3(h * 0.12f, h * 0.12f, h * 0.95f),
                    new Vector3(0f, baseY + h * 0.52f, h * 0.42f), accent * 0.75f);
        }

        private GameObject MakeBox(string name, Vector3 localSize, Vector3 localPosition, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;

            // A cosmetic must never block sight, carve the NavMesh, absorb a deploy tap or nudge
            // another agent. Every one of those has been a real bug in this project at some point.
            var collider = go.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            go.layer = gameObject.layer;

            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = localSize;

            var renderer = go.GetComponent<Renderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.material = new Material(accessoryTemplate) { color = color };

            return go;
        }

        private Material FindTemplateMaterial()
        {
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
                if (renderer.sharedMaterial != null) return renderer.sharedMaterial;

            Debug.LogWarning($"{name}: UnitClassVisual found no material to instance from - the class " +
                             "silhouette will not be built and every class will look identical.", this);
            return null;
        }

        /// <summary>
        /// The unit's own bounds expressed in its local space, so the accessory sizes correctly
        /// whatever WorldScale and the class scale multiplier have done to the root transform.
        /// </summary>
        private Bounds MeasureLocalBounds()
        {
            bool any = false;
            Bounds result = default;

            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                if (renderer.gameObject.name == AccessoryName) continue;

                Bounds world = renderer.bounds;
                Vector3 min = transform.InverseTransformPoint(world.min);
                Vector3 max = transform.InverseTransformPoint(world.max);

                var local = new Bounds((min + max) * 0.5f, Vector3.zero);
                local.Encapsulate(min);
                local.Encapsulate(max);

                if (!any) { result = local; any = true; }
                else result.Encapsulate(local);
            }

            // Falls back to the project's authored unit height rather than zero, so a prefab with
            // its renderers disabled still produces a visible accessory instead of nothing.
            if (!any) return new Bounds(Vector3.zero, Vector3.one * WorldScale.Metres(0.052f));

            return result;
        }
    }
}
