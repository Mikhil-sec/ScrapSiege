using UnityEngine;

namespace ScrapSiege.Vantage
{
    /// <summary>
    /// The vantage mechanic's arithmetic, kept free of MonoBehaviour/AR so it can be unit-tested
    /// (tuning these curves by feel on-device is slow; locking the behaviour in tests means a
    /// tuning pass can't silently invert the mapping).
    ///
    /// Convention throughout: vantage 0 = leaned in (low, precise, blinkered),
    /// vantage 1 = pulled back (high, imprecise, full overview).
    /// </summary>
    public static class VantageMath
    {
        /// <summary>
        /// Maps the phone's height above the board onto 0..1. Below lowHeight clamps to 0 (no
        /// reward for putting the phone on the table), above highHeight clamps to 1.
        /// </summary>
        public static float Normalized(float cameraWorldY, float boardWorldY, float lowHeight, float highHeight)
        {
            float span = highHeight - lowHeight;
            if (span <= 0.0001f) return 0f;

            float above = cameraWorldY - boardWorldY;
            return Mathf.Clamp01((above - lowHeight) / span);
        }

        /// <summary>
        /// Deploy scatter in metres. Linear rather than eased: the player needs to be able to
        /// predict it from their own posture, and a curve makes the relationship feel arbitrary.
        /// </summary>
        public static float ScatterRadius(float vantage01, float minRadius, float maxRadius)
        {
            return Mathf.Lerp(minRadius, maxRadius, Mathf.Clamp01(vantage01));
        }

        /// <summary>
        /// Whether the Rally order is currently issuable, with hysteresis so a player hovering
        /// exactly at the threshold doesn't get a button that strobes on and off. Pass the
        /// previous result as currentlyReady.
        /// </summary>
        public static bool EvaluateRallyReady(float vantage01, bool currentlyReady, float threshold, float hysteresis)
        {
            float half = Mathf.Max(hysteresis, 0f) * 0.5f;
            float enterAt = threshold + half;
            float exitAt = threshold - half;

            return currentlyReady ? vantage01 >= exitAt : vantage01 >= enterAt;
        }

        /// <summary>
        /// Exponential smoothing toward a target, framerate-independent. Raw camera height on a
        /// handheld phone is noisy enough that feeding it straight into scatter makes precise
        /// placement feel random rather than skilful - which would undermine the whole mechanic.
        /// </summary>
        public static float Smooth(float current, float target, float smoothingPerSecond, float deltaTime)
        {
            if (smoothingPerSecond <= 0f) return target;

            float t = 1f - Mathf.Exp(-smoothingPerSecond * deltaTime);
            return Mathf.Lerp(current, target, t);
        }
    }
}
