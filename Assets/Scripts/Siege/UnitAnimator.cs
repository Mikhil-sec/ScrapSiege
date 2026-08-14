using UnityEngine;
using UnityEngine.AI;
using ScrapSiege.Core;

namespace ScrapSiege.Siege
{
    /// <summary>
    /// Procedural march/attack animation driven entirely from the NavMeshAgent's own velocity.
    /// No rig, no skinning, no animation clips.
    ///
    /// This is a deliberate choice over skeletal animation: these units are ~5cm tall on a real
    /// table viewed through a phone, so rig deformation is invisible at that size while costing
    /// real authoring time and import risk. What *is* visible at 5cm is gross motion - legs
    /// swinging, the body bobbing, a lunge on attack - and all of that is a few lines of maths on
    /// transforms the model already exposes as separate objects.
    ///
    /// Everything is driven off actual speed rather than a timer, so a unit slowed by a crowd or
    /// stopped at a chokepoint visibly stops marching instead of moon-walking on the spot.
    ///
    /// The serialized values below are the defaults every class used before per-class motion
    /// existed. A <see cref="UnitMotionProfile"/> with overrideDefaults ticked replaces them at
    /// spawn - see <see cref="ApplyMotionProfile"/>.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class UnitAnimator : MonoBehaviour
    {
        [Header("Parts (auto-found by name if left empty)")]
        [SerializeField] private Transform body;
        [SerializeField] private Transform legLeft;
        [SerializeField] private Transform legRight;
        [SerializeField] private Transform weaponArm;

        [Header("March")]
        [Tooltip("Leg swing amplitude in degrees at full speed.")]
        [SerializeField] private float legSwingDegrees = 38f;

        [Tooltip("Stride cycles per metre travelled. Tied to distance, not time, so the gait stays " +
                 "in step with actual movement at any speed.")]
        [SerializeField] private float stridesPerMetre = 5.5f;

        [Tooltip("Vertical bob as a FRACTION of the unit's own height. Not metres: the parts are " +
                 "driven in the model's local space, whose scale depends on how the FBX happened to " +
                 "import, so a metre value here silently means something different per model.")]
        [SerializeField] private float bobHeightFraction = 0.03f;

        [Tooltip("Forward lean in degrees at full speed - sells momentum.")]
        [SerializeField] private float leanDegrees = 8f;

        [Header("Attack")]
        [Tooltip("Attack body travel as a fraction of the unit's own height.")]
        [SerializeField] private float lungeDistanceFraction = 0.12f;

        [SerializeField] private float lungeDurationSeconds = 0.25f;

        [Tooltip("Weapon-arm rotation at the peak of the attack, in degrees.")]
        [SerializeField] private float weaponSwingDegrees = 55f;

        [SerializeField] private AttackStyle attackStyle = AttackStyle.Thrust;

        private NavMeshAgent agent;
        private float strideCycle;
        private float lungeTimer;

        // The above fractions resolved into the body's local space at Awake.
        private float bobLocal;
        private float lungeLocal;

        private Vector3 bodyRestPosition;
        private Quaternion bodyRestRotation;
        private Quaternion legLeftRest;
        private Quaternion legRightRest;
        private Quaternion weaponArmRest;

        // Where the name lookup is allowed to search. Null means the whole hierarchy, which is only
        // correct before a class model has been swapped in - see Rebind.
        private Transform searchRoot;

        private void Awake()
        {
            Bind();
        }

        /// <summary>
        /// Re-finds the animated parts and re-caches their rest poses, searching ONLY inside
        /// <paramref name="root"/>.
        ///
        /// <para>Called by <see cref="UnitClassVisual"/> after it swaps in a class's own model.
        /// <see cref="Awake"/> runs at Instantiate, which is before the class is applied, so without
        /// this the animator keeps driving the transforms of the hidden shared trooper body.</para>
        ///
        /// <para><b>The scope argument is the whole fix, and its absence was a real shipped bug.</b>
        /// The old parameterless version cleared the fields and re-ran the same lookup - but that
        /// lookup walks <c>GetComponentsInChildren&lt;Transform&gt;()</c> and returns the FIRST name
        /// match, and the original <c>Visual</c> body is child 0 while the swapped-in model is
        /// appended last. So it re-found the exact same hidden <c>Torso</c>/<c>Leg_L</c>/<c>Leg_R</c>
        /// it was supposed to stop driving. Every class model in the game was therefore rigid while
        /// an invisible trooper marched inside it - which on enemy units, where
        /// <see cref="ScrapSiege.Vision.VisionTarget"/> was separately switching that body back on,
        /// showed up on device as two models stacked inside each other with only one of them moving.
        /// Re-running a name lookup is not the same as re-scoping it.</para>
        ///
        /// <para>The serialized overrides are cleared first: they are populated by name lookup, so
        /// leaving them pointing at the old body would make Bind's null checks skip the re-find
        /// entirely. An Inspector-assigned reference on a class-model unit is not a supported
        /// combination and would be a genuine authoring mistake.</para>
        /// </summary>
        public void Rebind(Transform root)
        {
            searchRoot = root;
            body = null;
            legLeft = null;
            legRight = null;
            weaponArm = null;
            Bind();
        }

        /// <summary>
        /// Adopts a class's authored gait and attack style. Called from
        /// <see cref="SiegeUnit.ApplyClass"/> before <see cref="Rebind"/>, so the new values are
        /// already in place when the rest poses and motion scale are resolved.
        ///
        /// A null or non-overriding profile is a no-op rather than a reset, so a class that has not
        /// authored one keeps the shared defaults above.
        /// </summary>
        public void ApplyMotionProfile(UnitMotionProfile profile)
        {
            if (profile == null || !profile.overrideDefaults) return;

            legSwingDegrees = profile.legSwingDegrees;
            stridesPerMetre = profile.stridesPerMetre;
            bobHeightFraction = profile.bobHeightFraction;
            leanDegrees = profile.leanDegrees;

            attackStyle = profile.attackStyle;
            lungeDistanceFraction = profile.attackTravelFraction;
            lungeDurationSeconds = Mathf.Max(0.05f, profile.attackDurationSeconds);
            weaponSwingDegrees = profile.weaponSwingDegrees;

            // The fractions above are resolved into local space against the measured model, so a
            // profile applied after the first Bind has to re-resolve or it silently keeps the old
            // amplitudes. Cheap, and it makes call order not matter.
            ResolveMotionScale();
        }

        private void Bind()
        {
            if (agent == null) agent = GetComponent<NavMeshAgent>();

            // Name lookup keeps this working straight off the Blender FBX import without needing
            // four Inspector drags on every prefab variant.
            if (body == null) body = FindChild("Torso");
            if (legLeft == null) legLeft = FindChild("Leg_L");
            if (legRight == null) legRight = FindChild("Leg_R");
            if (weaponArm == null) weaponArm = FindChild("WeaponArm");

            if (body != null)
            {
                bodyRestPosition = body.localPosition;
                bodyRestRotation = body.localRotation;
            }
            if (legLeft != null) legLeftRest = legLeft.localRotation;
            if (legRight != null) legRightRest = legRight.localRotation;
            if (weaponArm != null) weaponArmRest = weaponArm.localRotation;

            if (body == null && legLeft == null && legRight == null)
                Debug.LogWarning($"{name}: UnitAnimator found none of Torso/Leg_L/Leg_R - the unit will not animate. Check the model's child names.", this);

            ResolveMotionScale();
        }

        /// <summary>
        /// Converts the authored fractions into the body's local space.
        ///
        /// This has to be derived, never hard-coded. The parts are moved via localPosition, so the
        /// units involved are whatever the imported FBX's own scale happens to be - and that changed
        /// by a factor of ~54 when the model was re-exported. The old metre-valued bobHeight of 0.02
        /// then resolved to 0.088m of world travel on a 0.052m unit: the torso (and everything
        /// parented to it - head, arm, spear) launched 1.7x the unit's own height on every stride,
        /// visibly tearing the figure in half and flinging the top out of frame.
        ///
        /// Measuring the real world height and dividing by the real lossyScale means the gait looks
        /// the same at any prefab scale and survives the next re-export.
        /// </summary>
        private void ResolveMotionScale()
        {
            if (body == null) return;

            float heightWorld = MeasureWorldHeight();
            float localPerWorld = body.parent != null ? body.parent.lossyScale.y : transform.lossyScale.y;
            if (Mathf.Abs(localPerWorld) < 1e-6f) localPerWorld = 1f;

            bobLocal = bobHeightFraction * heightWorld / localPerWorld;
            lungeLocal = lungeDistanceFraction * heightWorld / localPerWorld;
        }

        private float MeasureWorldHeight()
        {
            bool first = true;
            Bounds bounds = new Bounds();
            foreach (var renderer in GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled) continue;
                if (first) { bounds = renderer.bounds; first = false; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return first ? 0f : bounds.size.y;
        }

        /// <summary>
        /// Finds an animated part by name, restricted to <see cref="searchRoot"/> once a class model
        /// has claimed one. The whole-hierarchy fallback is for the legacy scan/Fortify path, where
        /// no swap ever happens and the only body present is the right one.
        /// </summary>
        private Transform FindChild(string childName)
        {
            Transform root = searchRoot != null ? searchRoot : transform;

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == childName) return t;

            return null;
        }

        private void Update()
        {
            float speed = agent != null ? agent.velocity.magnitude : 0f;
            float normalized = agent != null && agent.speed > 0.001f
                ? Mathf.Clamp01(speed / agent.speed)
                : 0f;

            AdvanceStride(speed);
            ApplyLegs(normalized);
            ApplyBody(normalized);
            TickAttack();
        }

        /// <summary>
        /// Advances the gait by distance travelled rather than elapsed time, so slow units take
        /// slow steps instead of the same steps more often.
        /// </summary>
        private void AdvanceStride(float speed)
        {
            // Note the DIVISION: this is the one value that scales inversely. `speed` is in scaled
            // world units, so a unit covers WorldScale.Scale times more Unity metres for the same
            // real distance - left alone, the gait would run that many times too fast and the legs
            // would blur. stridesPerMetre stays authored per REAL metre travelled.
            strideCycle += speed * (stridesPerMetre / WorldScale.Scale) * Time.deltaTime * Mathf.PI * 2f;
            if (strideCycle > Mathf.PI * 2f) strideCycle -= Mathf.PI * 2f;
        }

        private void ApplyLegs(float normalized)
        {
            if (normalized < 0.01f)
            {
                // Settle back to a stand rather than freezing mid-stride.
                if (legLeft != null) legLeft.localRotation = Quaternion.Slerp(legLeft.localRotation, legLeftRest, Time.deltaTime * 10f);
                if (legRight != null) legRight.localRotation = Quaternion.Slerp(legRight.localRotation, legRightRest, Time.deltaTime * 10f);
                return;
            }

            float swing = Mathf.Sin(strideCycle) * legSwingDegrees * normalized;
            if (legLeft != null) legLeft.localRotation = legLeftRest * Quaternion.Euler(swing, 0f, 0f);
            if (legRight != null) legRight.localRotation = legRightRest * Quaternion.Euler(-swing, 0f, 0f);
        }

        private void ApplyBody(float normalized)
        {
            if (body == null) return;

            // Bob at twice stride frequency - the body rises once per footfall, not once per cycle.
            float bob = Mathf.Abs(Mathf.Sin(strideCycle)) * bobLocal * normalized;
            float phase = AttackPhase();

            // Where the attack drives the body. Recoil is the one that goes BACKWARD: a unit that
            // fires a tracer has nothing to reach for, so a forward lunge reads as it trying to stab
            // something on the other side of the board.
            float travel = phase * lungeLocal * (attackStyle == AttackStyle.Recoil ? -0.7f : 1f);

            // A brace is a shove from behind the shield, so the body drops into it as it goes.
            float dip = attackStyle == AttackStyle.Brace ? -phase * lungeLocal * 0.35f : 0f;

            body.localPosition = bodyRestPosition + new Vector3(0f, bob + dip, travel);

            // A swipe carries the whole torso round with the arc; everything else only leans.
            float twist = attackStyle == AttackStyle.Swipe ? phase * weaponSwingDegrees * 0.35f : 0f;
            body.localRotation = bodyRestRotation * Quaternion.Euler(leanDegrees * normalized, twist, 0f);
        }

        /// <summary>0 at rest, rising to 1 at the peak of the attack and back to 0. One shared curve
        /// so every style stays in step with the same tick.</summary>
        private float AttackPhase()
        {
            if (lungeTimer <= 0f) return 0f;
            return Mathf.Sin((1f - lungeTimer / lungeDurationSeconds) * Mathf.PI);
        }

        private void TickAttack()
        {
            if (lungeTimer <= 0f) return;
            lungeTimer = Mathf.Max(0f, lungeTimer - Time.deltaTime);

            if (weaponArm == null) return;

            float phase = AttackPhase();
            float swing = phase * weaponSwingDegrees;

            switch (attackStyle)
            {
                // The muzzle kicks UP and the arm rides back with it, then settles. Reads as a shot
                // being fired rather than a weapon being swung at something out of reach.
                case AttackStyle.Recoil:
                    weaponArm.localRotation = weaponArmRest * Quaternion.Euler(swing * 0.45f, 0f, 0f);
                    break;

                // A shove: the shield arm barely rotates, because the power comes from the body.
                case AttackStyle.Brace:
                    weaponArm.localRotation = weaponArmRest * Quaternion.Euler(-swing * 0.35f, 0f, 0f);
                    break;

                // A lateral arc across the body - the only style that moves on Y, which is what
                // makes it read as a different weapon and not just a bigger stab.
                case AttackStyle.Swipe:
                    weaponArm.localRotation = weaponArmRest * Quaternion.Euler(-swing * 0.3f, -swing, 0f);
                    break;

                case AttackStyle.Thrust:
                default:
                    weaponArm.localRotation = weaponArmRest * Quaternion.Euler(-swing, 0f, 0f);
                    break;
            }
        }

        /// <summary>Call when the unit strikes something - plays a one-shot attack motion.</summary>
        public void PlayAttack()
        {
            lungeTimer = lungeDurationSeconds;
        }
    }
}
