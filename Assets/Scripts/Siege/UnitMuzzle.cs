using System.Collections.Generic;
using UnityEngine;

namespace ScrapSiege.Siege
{
    /// <summary>
    /// Where a ranged unit's shot actually leaves the model.
    ///
    /// <para><b>The bug this replaces.</b> <see cref="SiegeUnit"/> used to fire from
    /// <c>transform.position + Vector3.up * (engagementRadius * 0.12f)</c> - a height derived from
    /// the class's <i>reach</i>, which has no relationship whatsoever to where the model's barrel
    /// is. On a 0.60m board that put the Turret's muzzle around 1.7cm up a ~5.7cm figure, i.e.
    /// visibly below its own barrel, which is exactly the "tracer looks like it is firing from below
    /// the gun barrel" reported from device on 2026-08-13. The Marksman had the same fault, hidden
    /// only by a longer reach making the number accidentally larger.</para>
    ///
    /// <para><b>Measured, never typed.</b> Same standing rule as <see cref="UnitClassVisual"/> and
    /// <see cref="UnitAnimator"/>: the fire point comes from the real renderer bounds of the real
    /// weapon part, so a re-export at a different scale, or a Veteran skin with a longer barrel,
    /// cannot silently desynchronise the tracer from the art. This project has paid for hand-typed
    /// sizes on this prefab three times now.</para>
    ///
    /// <para>Recomputed per shot rather than cached as an offset, so the muzzle follows the weapon
    /// arm while <see cref="UnitAnimator"/> is animating it. That costs one bounds read per attack
    /// tick (0.5-0.9s), not per frame.</para>
    /// </summary>
    public class UnitMuzzle : MonoBehaviour
    {
        /// <summary>
        /// Weapon part names to look for, best first. The first group with any match wins, so a
        /// turret's barrels are preferred over the generic arm that holds them.
        ///
        /// These are the names the shipped FBXs actually expose - verified against the imported
        /// assets, not assumed. Adding a new weapon part to a model means adding it here, and the
        /// fallback below means forgetting to is a slightly-wrong muzzle rather than no tracer.
        /// </summary>
        private static readonly string[][] PartNameGroups =
        {
            new[] { "BarrelL", "BarrelC", "BarrelR", "Barrel" },
            new[] { "Rifle" },
            new[] { "Spear", "Halberd", "Blade" },
            new[] { "WeaponArm" },
        };

        [Tooltip("Fallback muzzle height as a fraction of the unit's own measured height, used when " +
                 "no weapon part can be found. Roughly shoulder height on the shipped models.")]
        [Range(0f, 1f)]
        [SerializeField] private float fallbackHeightFraction = 0.62f;

        private readonly List<Renderer> muzzleParts = new List<Renderer>();
        private int nextPart;

        private void Awake()
        {
            Rebind(null);
        }

        /// <summary>
        /// Re-finds the weapon parts, restricted to <paramref name="root"/> when a class model has
        /// been swapped in. Null searches the whole hierarchy, which is correct only before a swap.
        ///
        /// <para>Scoped for the same reason <see cref="UnitAnimator.Rebind"/> is: the original
        /// trooper body is still present (hidden) under the unit, it owns a <c>Spear</c> and a
        /// <c>WeaponArm</c>, and an unscoped name lookup finds those first because they are earlier
        /// in the hierarchy. That would fire every tracer from the invisible body.</para>
        /// </summary>
        public void Rebind(Transform root)
        {
            muzzleParts.Clear();
            nextPart = 0;

            Transform searchRoot = root != null ? root : transform;

            foreach (var group in PartNameGroups)
            {
                foreach (var candidate in searchRoot.GetComponentsInChildren<Renderer>(true))
                {
                    if (candidate == null || !candidate.enabled) continue;

                    foreach (var partName in group)
                    {
                        if (candidate.gameObject.name != partName) continue;
                        muzzleParts.Add(candidate);
                        break;
                    }
                }

                if (muzzleParts.Count > 0) return;
            }
        }

        /// <summary>
        /// The point the next shot leaves from. Multi-barrel models alternate, which costs nothing
        /// and makes a twin-barrel turret read as a twin-barrel turret rather than a single gun.
        /// </summary>
        public Vector3 FirePoint()
        {
            if (muzzleParts.Count == 0) return FallbackPoint();

            // Round-robin, skipping anything destroyed since the last shot.
            for (int attempt = 0; attempt < muzzleParts.Count; attempt++)
            {
                Renderer part = muzzleParts[nextPart];
                nextPart = (nextPart + 1) % muzzleParts.Count;

                if (part == null || !part.enabled) continue;
                return TipOf(part.bounds);
            }

            return FallbackPoint();
        }

        /// <summary>
        /// The far end of a part along the unit's facing. Projecting the bounds' extent onto forward
        /// gives the tip whatever way the model is turned, without needing the barrel to be axis
        /// aligned or the FBX to agree about which axis is "along the gun".
        /// </summary>
        private Vector3 TipOf(Bounds bounds)
        {
            Vector3 forward = transform.forward;
            Vector3 extents = bounds.extents;

            float reach = Mathf.Abs(extents.x * forward.x)
                        + Mathf.Abs(extents.y * forward.y)
                        + Mathf.Abs(extents.z * forward.z);

            return bounds.center + forward * reach;
        }

        private Vector3 FallbackPoint()
        {
            bool any = false;
            Bounds bounds = default;

            foreach (var renderer in GetComponentsInChildren<Renderer>())
            {
                if (renderer == null || !renderer.enabled) continue;
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }

            if (!any) return transform.position;

            return new Vector3(
                bounds.center.x,
                Mathf.Lerp(bounds.min.y, bounds.max.y, fallbackHeightFraction),
                bounds.center.z);
        }
    }
}
