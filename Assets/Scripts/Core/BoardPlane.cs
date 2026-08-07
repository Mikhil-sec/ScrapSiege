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

        /// <summary>World-space Y of the playing surface.</summary>
        public float Height { get; private set; }

        /// <summary>World-space centre of the board, used by the AI and by arc facing.</summary>
        public Vector3 Centre { get; private set; }

        /// <summary>False until a real surface has been established - readers should degrade gracefully.</summary>
        public bool IsEstablished { get; private set; }

        private void Awake()
        {
            Height = fallbackHeight;
            Centre = new Vector3(0f, fallbackHeight, 0f);
        }

        public void SetBoard(Vector3 centre)
        {
            Centre = centre;
            Height = centre.y;
            IsEstablished = true;
        }

        /// <summary>How far above the board surface a world point sits. Negative below.</summary>
        public float HeightAbove(Vector3 worldPoint) => worldPoint.y - Height;
    }
}
