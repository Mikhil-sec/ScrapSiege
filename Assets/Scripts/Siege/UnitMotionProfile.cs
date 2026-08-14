using System;
using UnityEngine;

namespace ScrapSiege.Siege
{
    /// <summary>
    /// How a class swings its weapon. Purely visual, but it is what stops every unit in the game
    /// reading as the same soldier with a different hat.
    ///
    /// <para>Reported from device 2026-08-13: "the enemy marksman attack animation seems a little
    /// weird". Two separate things were wrong, and this is the second one - a marksman was playing
    /// the spear THRUST authored for the original trooper, so a figure holding a rifle lunged
    /// forward and stabbed with it. (The first was the model-stacking bug in
    /// <see cref="ScrapSiege.Vision.VisionTarget"/>.)</para>
    /// </summary>
    public enum AttackStyle
    {
        /// <summary>Body drives forward, weapon arm swings down and through. Spears, blades.</summary>
        Thrust,

        /// <summary>
        /// Body kicks BACKWARD and the muzzle rises, then settles. Rifles and turret barrels - the
        /// only honest way to animate something that fires a tracer rather than reaching the target.
        /// </summary>
        Recoil,

        /// <summary>Shield shoves out and the body dips behind it. A shove, not a stab.</summary>
        Brace,

        /// <summary>A lateral arc across the body, with a matching twist. Halberds, wide blades.</summary>
        Swipe,
    }

    /// <summary>
    /// A class's gait and attack motion.
    ///
    /// <para><b>Why per class, and per skin.</b> These units are ~5cm on a real table, so rig
    /// deformation is invisible but gross motion is not (see <see cref="UnitAnimator"/>). Gross
    /// motion is therefore the entire animation budget, and spending it identically on every class
    /// wastes the one channel that still reads at that size. A Bulwark should plod, a Saboteur
    /// should scuttle, and a Veteran skin the player paid for should move differently enough to be
    /// recognisable from across the table.</para>
    ///
    /// <para><b>Everything vertical or forward is a FRACTION of the unit's own height</b>, never
    /// metres. The parts are driven through <c>localPosition</c>, whose units depend entirely on how
    /// the FBX happened to import - and this project has already shipped a bob authored in metres
    /// against a model whose scale later changed by ~54x, which launched the torso 1.7x the unit's
    /// own height on every stride.</para>
    ///
    /// <para>Leave <see cref="overrideDefaults"/> off and the animator keeps its own serialized
    /// values, so an unauthored class behaves exactly as it did before this existed.</para>
    /// </summary>
    [Serializable]
    public class UnitMotionProfile
    {
        [Tooltip("Off means 'use the UnitAnimator's own serialized values'. Everything below is " +
                 "ignored until this is ticked, so adding this field changed no existing unit.")]
        public bool overrideDefaults;

        [Header("Gait")]
        [Tooltip("Leg swing amplitude in degrees at full speed. Low reads as a trudge, high as a run.")]
        [Range(0f, 70f)]
        public float legSwingDegrees = 38f;

        [Tooltip("Stride cycles per REAL metre travelled. Tied to distance rather than time, so a " +
                 "slow unit takes slow steps instead of the same steps less often.")]
        [Range(1f, 14f)]
        public float stridesPerMetre = 5.5f;

        [Tooltip("Vertical bob as a fraction of the unit's own height.")]
        [Range(0f, 0.12f)]
        public float bobHeightFraction = 0.03f;

        [Tooltip("Forward lean in degrees at full speed - sells momentum. Negative leans back, " +
                 "which reads as a heavy unit resisting its own mass.")]
        [Range(-15f, 25f)]
        public float leanDegrees = 8f;

        [Header("Attack")]
        public AttackStyle attackStyle = AttackStyle.Thrust;

        [Tooltip("How far the body travels during the attack, as a fraction of the unit's own " +
                 "height. Recoil applies it backward; the others forward.")]
        [Range(0f, 0.3f)]
        public float attackTravelFraction = 0.12f;

        [Tooltip("Seconds the whole attack motion takes. Must stay under the class's " +
                 "attackTickSeconds or the next blow interrupts this one mid-swing.")]
        [Range(0.05f, 0.9f)]
        public float attackDurationSeconds = 0.25f;

        [Tooltip("Weapon-arm rotation at the peak of the attack, in degrees. Read differently per " +
                 "style: Thrust and Brace swing it down and through, Recoil kicks the muzzle up, " +
                 "Swipe arcs it across the body.")]
        [Range(0f, 120f)]
        public float weaponSwingDegrees = 55f;
    }
}
