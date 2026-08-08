using UnityEngine;

namespace ScrapSiege.Core
{
    /// <summary>
    /// The one place that answers "where is the board, and how high is its surface?".
    ///
    /// Vantage needs the board's world Y to know how far the phone is held above it, and it must
    /// not care whether that height came from a locked AR plane (today) or from a placed
    /// LevelDefinition board (the authored-map flow that replaces Fortify). Whoever establishes
    /// the board calls SetBoard(); everything downstream reads this.
    ///
    /// Deliberately a plain component with no AR dependency so it stays testable and so the
    /// upcoming board-placement flow can drive it without touching the vantage/vision code.
    /// </summary>
    public class BoardPlane : MonoBehaviour
    {
        [Tooltip("Used before a real board exists (Editor play without AR). Height in metres.")]
        [SerializeField] private float fallbackHeight;

        [Tooltip("Board length assumed before a real board is placed - and by the legacy scan/Fortify " +
                 "flow, which has no authored board to measure. Authored in REAL metres; converted " +
                 "into the scaled AR world at Awake.")]
        [SerializeField] private float fallbackLength = 0.6f;

        /// <summary>World-space Y of the playing surface.</summary>
        public float Height { get; private set; }

        /// <summary>World-space centre of the board, used by the AI and by arc facing.</summary>
        public Vector3 Centre { get; private set; }

        /// <summary>
        /// The board's real length in metres.
        ///
        /// This is the denominator for every gameplay distance. Levels are authored in normalised
        /// space and land on whatever size table the player chose, so anything tuned as an absolute
        /// metre value is really a hidden assumption about board size - and several of them were
        /// wrong by 3-5x once boards got small (a sentry covering a third of the map, a rally tap
        /// snapping 42% of the board away). Read this and scale.
        /// </summary>
        public float Length { get; private set; }

        /// <summary>False until a real surface has been established - readers should degrade gracefully.</summary>
        public bool IsEstablished { get; private set; }

        private void Awake()
        {
            Height = WorldScale.Metres(fallbackHeight);
            Centre = new Vector3(0f, Height, 0f);
            Length = WorldScale.Metres(fallbackLength);
        }

        /// <summary>Legacy scan/Fortify entry point - there is no authored board, so length is left at the fallback.</summary>
        public void SetBoard(Vector3 centre) => SetBoard(centre, Length);

        public void SetBoard(Vector3 centre, float length)
        {
            Centre = centre;
            Height = centre.y;
            if (length > WorldScale.Metres(0.01f)) Length = length;
            IsEstablished = true;
        }

        /// <summary>How far above the board surface a world point sits. Negative below.</summary>
        public float HeightAbove(Vector3 worldPoint) => worldPoint.y - Height;
    }
}
