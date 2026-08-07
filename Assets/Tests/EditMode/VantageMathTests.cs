using NUnit.Framework;
using UnityEngine;
using ScrapSiege.Vantage;

namespace ScrapSiege.Tests
{
    /// <summary>
    /// Locks in the vantage mapping. These curves get tuned by feel on-device, which is slow and
    /// easy to get subtly backwards - a tuning pass that inverted the mapping (pulled back becoming
    /// the *precise* posture) would still "work" and would quietly destroy the mechanic.
    /// </summary>
    public class VantageMathTests
    {
        private const float LowHeight = 0.20f;
        private const float HighHeight = 0.65f;

        [Test]
        public void Normalized_AtOrBelowLeanedInHeight_IsZero()
        {
            Assert.AreEqual(0f, VantageMath.Normalized(0.20f, 0f, LowHeight, HighHeight), 1e-4f);
            Assert.AreEqual(0f, VantageMath.Normalized(0.05f, 0f, LowHeight, HighHeight), 1e-4f,
                "Putting the phone on the table should clamp to fully leaned in, not go negative.");
        }

        [Test]
        public void Normalized_AtOrAbovePulledBackHeight_IsOne()
        {
            Assert.AreEqual(1f, VantageMath.Normalized(0.65f, 0f, LowHeight, HighHeight), 1e-4f);
            Assert.AreEqual(1f, VantageMath.Normalized(2.0f, 0f, LowHeight, HighHeight), 1e-4f);
        }

        [Test]
        public void Normalized_IsMeasuredRelativeToBoardHeight_NotWorldOrigin()
        {
            // A table 0.75 m off the floor must behave identically to a board at world zero.
            float atFloorLevel = VantageMath.Normalized(0.40f, 0f, LowHeight, HighHeight);
            float onATable = VantageMath.Normalized(1.15f, 0.75f, LowHeight, HighHeight);

            Assert.AreEqual(atFloorLevel, onATable, 1e-4f);
        }

        [Test]
        public void Normalized_DegenerateBand_DoesNotDivideByZero()
        {
            Assert.AreEqual(0f, VantageMath.Normalized(5f, 0f, 0.3f, 0.3f), 1e-4f);
        }

        [Test]
        public void ScatterRadius_GrowsWithVantage()
        {
            float leanedIn = VantageMath.ScatterRadius(0f, 0.005f, 0.10f);
            float pulledBack = VantageMath.ScatterRadius(1f, 0.005f, 0.10f);

            Assert.AreEqual(0.005f, leanedIn, 1e-4f);
            Assert.AreEqual(0.10f, pulledBack, 1e-4f);
            Assert.Less(leanedIn, pulledBack, "Leaning in must be the precise posture, not the loose one.");
        }

        [Test]
        public void EvaluateRallyReady_UsesHysteresisToAvoidStrobing()
        {
            const float threshold = 0.6f;
            const float hysteresis = 0.1f;

            // Rising through the band: must clear threshold + half the band to switch on.
            Assert.IsFalse(VantageMath.EvaluateRallyReady(0.62f, currentlyReady: false, threshold, hysteresis),
                "Just past the threshold should not arm yet while rising.");
            Assert.IsTrue(VantageMath.EvaluateRallyReady(0.66f, currentlyReady: false, threshold, hysteresis));

            // Falling back through the same band: must drop below threshold - half to switch off.
            Assert.IsTrue(VantageMath.EvaluateRallyReady(0.57f, currentlyReady: true, threshold, hysteresis),
                "Dipping slightly below the threshold should not immediately disarm.");
            Assert.IsFalse(VantageMath.EvaluateRallyReady(0.53f, currentlyReady: true, threshold, hysteresis));
        }

        [Test]
        public void Smooth_MovesTowardTargetWithoutOvershooting()
        {
            float value = 0f;
            for (int i = 0; i < 200; i++)
                value = VantageMath.Smooth(value, 1f, 8f, 1f / 60f);

            Assert.AreEqual(1f, value, 0.01f);
            Assert.LessOrEqual(value, 1f, "Exponential smoothing must never overshoot its target.");
        }

        [Test]
        public void Smooth_WithZeroSmoothing_SnapsImmediately()
        {
            Assert.AreEqual(1f, VantageMath.Smooth(0f, 1f, 0f, 1f / 60f), 1e-4f);
        }

        [Test]
        public void Smooth_IsFramerateIndependent()
        {
            // Same wall-clock duration at 30 fps and 120 fps should land in the same place;
            // otherwise the mechanic feels different on a slower phone.
            float at30 = 0f;
            for (int i = 0; i < 30; i++) at30 = VantageMath.Smooth(at30, 1f, 8f, 1f / 30f);

            float at120 = 0f;
            for (int i = 0; i < 120; i++) at120 = VantageMath.Smooth(at120, 1f, 8f, 1f / 120f);

            Assert.AreEqual(at30, at120, 0.01f);
        }
    }
}
