using UnityEngine;

namespace ScrapSiege.Vision
{
    /// <summary>How much of an enemy the player can currently make out from where they are standing.</summary>
    public enum RevealTier
    {
        /// <summary>No sample point visible. Only a drifting last-known-position ghost is shown.</summary>
        Hidden = 0,

        /// <summary>A sliver visible - just enough to read as "something is there".</summary>
        Faint = 1,

        /// <summary>Most of it visible, still partly behind cover.</summary>
        Partial = 2,

        /// <summary>Fully in the open from this viewpoint.</summary>
        Full = 3,
    }

    /// <summary>
    /// Line-of-sight arithmetic with no Unity scene dependency, so the reveal ladder can be
    /// tested directly.
    ///
    /// The grading exists because a binary visible/invisible flip reads as a rendering bug rather
    /// than a designed mechanic, and because it makes *half* peeking meaningful - leaning until
    /// an enemy goes from Faint to Partial is real information gain, which is exactly the physical
    /// behaviour the game wants to reward.
    /// </summary>
    public static class VisionMath
    {
        /// <summary>
        /// Maps "how many of this target's sample points are unobstructed" onto a reveal tier.
        /// Generalised over the sample count so the three-point scheme can be widened later
        /// without rewriting callers.
        /// </summary>
        public static RevealTier TierFromVisiblePoints(int visiblePoints, int totalPoints)
        {
            if (totalPoints <= 0 || visiblePoints <= 0) return RevealTier.Hidden;
            if (visiblePoints >= totalPoints) return RevealTier.Full;

            // Anything in between splits at the midpoint: below half reads as a sliver (Faint),
            // at or above half reads as mostly-there (Partial).
            float fraction = (float)visiblePoints / totalPoints;
            return fraction < 0.5f ? RevealTier.Faint : RevealTier.Partial;
        }

        /// <summary>Render alpha for each tier. Hidden is fully transparent - the ghost stands in for it.</summary>
        public static float AlphaForTier(RevealTier tier)
        {
            switch (tier)
            {
                case RevealTier.Faint: return 0.25f;
                case RevealTier.Partial: return 0.6f;
                case RevealTier.Full: return 1f;
                case RevealTier.Hidden:
                default: return 0f;
            }
        }

        /// <summary>
        /// Where a stale ghost has drifted to. The ghost continues along the heading the enemy was
        /// last seen moving on, so old intel is actively *wrong* rather than merely old - which is
        /// what gives re-peeking real value instead of making it optional.
        /// </summary>
        public static Vector3 DriftedGhostPosition(
            Vector3 lastSeenPosition,
            Vector3 lastSeenVelocity,
            float secondsSinceSeen,
            float maxDriftSeconds)
        {
            float drift = Mathf.Clamp(secondsSinceSeen, 0f, Mathf.Max(maxDriftSeconds, 0f));
            return lastSeenPosition + lastSeenVelocity * drift;
        }

        /// <summary>
        /// Ghost opacity over time - fades to nothing by maxAgeSeconds so the board doesn't
        /// accumulate permanent phantom markers.
        /// </summary>
        public static float GhostAlpha(float secondsSinceSeen, float maxAgeSeconds, float peakAlpha)
        {
            if (maxAgeSeconds <= 0f) return 0f;
            float remaining = 1f - Mathf.Clamp01(secondsSinceSeen / maxAgeSeconds);
            return remaining * peakAlpha;
        }
    }
}
