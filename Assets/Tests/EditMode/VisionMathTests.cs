using NUnit.Framework;
using UnityEngine;
using ScrapSiege.Vision;

namespace ScrapSiege.Tests
{
    /// <summary>
    /// Locks in the graded-reveal ladder and the ghost drift. The grading is what stops line of
    /// sight reading as a rendering bug, so a change that collapses it back to binary should fail
    /// loudly rather than ship.
    /// </summary>
    public class VisionMathTests
    {
        [Test]
        public void TierFromVisiblePoints_ThreePointScheme_MatchesDesignLadder()
        {
            Assert.AreEqual(RevealTier.Hidden, VisionMath.TierFromVisiblePoints(0, 3));
            Assert.AreEqual(RevealTier.Faint, VisionMath.TierFromVisiblePoints(1, 3));
            Assert.AreEqual(RevealTier.Partial, VisionMath.TierFromVisiblePoints(2, 3));
            Assert.AreEqual(RevealTier.Full, VisionMath.TierFromVisiblePoints(3, 3));
        }

        [Test]
        public void TierFromVisiblePoints_IsNotBinary()
        {
            // The whole point of grading: at least two distinct states exist between fully hidden
            // and fully visible.
            var partialTiers = new[]
            {
                VisionMath.TierFromVisiblePoints(1, 3),
                VisionMath.TierFromVisiblePoints(2, 3),
            };

            CollectionAssert.AllItemsAreUnique(partialTiers);
            CollectionAssert.DoesNotContain(partialTiers, RevealTier.Hidden);
            CollectionAssert.DoesNotContain(partialTiers, RevealTier.Full);
        }

        [Test]
        public void TierFromVisiblePoints_HandlesDegenerateCounts()
        {
            Assert.AreEqual(RevealTier.Hidden, VisionMath.TierFromVisiblePoints(0, 0));
            Assert.AreEqual(RevealTier.Hidden, VisionMath.TierFromVisiblePoints(-1, 3));
            Assert.AreEqual(RevealTier.Full, VisionMath.TierFromVisiblePoints(5, 3),
                "More visible points than sampled should saturate, not wrap to a lower tier.");
        }

        [Test]
        public void AlphaForTier_IncreasesMonotonically()
        {
            float hidden = VisionMath.AlphaForTier(RevealTier.Hidden);
            float faint = VisionMath.AlphaForTier(RevealTier.Faint);
            float partial = VisionMath.AlphaForTier(RevealTier.Partial);
            float full = VisionMath.AlphaForTier(RevealTier.Full);

            Assert.AreEqual(0f, hidden, 1e-4f);
            Assert.AreEqual(1f, full, 1e-4f);
            Assert.Less(hidden, faint);
            Assert.Less(faint, partial);
            Assert.Less(partial, full);
        }

        [Test]
        public void DriftedGhostPosition_MovesAlongLastKnownHeading()
        {
            var lastSeen = new Vector3(0f, 0f, 0f);
            var velocity = new Vector3(0.1f, 0f, 0f);

            Vector3 after2s = VisionMath.DriftedGhostPosition(lastSeen, velocity, 2f, maxDriftSeconds: 3f);

            Assert.AreEqual(0.2f, after2s.x, 1e-4f, "Stale intel should be wrong, not merely old.");
        }

        [Test]
        public void DriftedGhostPosition_StopsDriftingAfterMaxDrift()
        {
            var velocity = new Vector3(0.1f, 0f, 0f);

            Vector3 atCap = VisionMath.DriftedGhostPosition(Vector3.zero, velocity, 3f, maxDriftSeconds: 3f);
            Vector3 wellPast = VisionMath.DriftedGhostPosition(Vector3.zero, velocity, 30f, maxDriftSeconds: 3f);

            Assert.AreEqual(atCap.x, wellPast.x, 1e-4f,
                "An unbounded drift would send ghosts off the table entirely.");
        }

        [Test]
        public void DriftedGhostPosition_StaticTargetDoesNotDrift()
        {
            // A sentry that was at a chokepoint is still at that chokepoint - its ghost must sit still.
            Vector3 drifted = VisionMath.DriftedGhostPosition(new Vector3(1f, 0f, 2f), Vector3.zero, 5f, 3f);

            Assert.AreEqual(new Vector3(1f, 0f, 2f), drifted);
        }

        [Test]
        public void GhostAlpha_FadesToZeroOverLifetime()
        {
            Assert.AreEqual(0.5f, VisionMath.GhostAlpha(0f, 6f, 0.5f), 1e-4f);
            Assert.AreEqual(0.25f, VisionMath.GhostAlpha(3f, 6f, 0.5f), 1e-4f);
            Assert.AreEqual(0f, VisionMath.GhostAlpha(6f, 6f, 0.5f), 1e-4f);
        }

        [Test]
        public void GhostAlpha_NeverGoesNegativePastLifetime()
        {
            Assert.AreEqual(0f, VisionMath.GhostAlpha(100f, 6f, 0.5f), 1e-4f);
        }
    }
}
