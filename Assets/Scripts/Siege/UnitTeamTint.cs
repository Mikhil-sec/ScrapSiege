using UnityEngine;
using ScrapSiege.Core;

namespace ScrapSiege.Siege
{
    /// <summary>
    /// Paints a unit in its side's colour at spawn, so one authored model serves both armies.
    ///
    /// Which side a figure belongs to is the single most important thing to read on a crowded
    /// board, and at ~5cm it has to come from colour rather than silhouette. Doing it here rather
    /// than by authoring a second model means the AI commander's units (still to be built) get the
    /// same treatment for free, and means the two sides can never drift apart visually.
    ///
    /// Crest and shield slots are deliberately left alone by <see cref="MaterialSlots"/> - they are
    /// the high-contrast highlight that keeps a figure legible against its own team colour.
    /// </summary>
    public class UnitTeamTint : MonoBehaviour
    {
        [Tooltip("Any opaque material using the active render pipeline - team colours are instanced from it.")]
        [SerializeField] private Material baseMaterial;

        [SerializeField] private Color teamColor = new Color(0.16f, 0.38f, 0.72f);

        private void Awake()
        {
            Apply();
        }

        /// <summary>Re-tints now. Call after swapping sides, if capturable garrisons ever land.</summary>
        public void Apply()
        {
            if (baseMaterial == null)
            {
                Debug.LogWarning($"{name}: UnitTeamTint has no Base Material - the unit will keep its authored colours " +
                                 "and both sides will look identical.", this);
                return;
            }

            MaterialSlots.Repaint(gameObject, baseMaterial, teamColor);
        }

        public void SetTeamColor(Color color)
        {
            teamColor = color;
            Apply();
        }
    }
}
