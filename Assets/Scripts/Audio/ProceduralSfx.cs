using UnityEngine;

namespace ScrapSiege.Audio
{
    /// <summary>The game's sound effects, referenced by name rather than by clip asset.</summary>
    public enum Sfx
    {
        UiTap,
        Deploy,
        SentryFire,
        UnitDeath,
        BaseHit,
        Rally,
        PhaseChange,
        Victory,
        Defeat,
    }

    /// <summary>
    /// Builds every sound effect in the game from arithmetic at startup, rather than importing
    /// recorded audio files.
    ///
    /// <para>This is a deliberate choice, not a placeholder. Recorded SFX would mean either
    /// licensed third-party audio in a public repo (an attribution and redistribution problem for a
    /// jam submission) or hand-recorded clips this project has no way to produce. Synthesis costs a
    /// few hundred kilobytes of generated PCM at load, keeps the repo free of binary blobs, and
    /// matches the flat-shaded low-poly art direction - the game already looks synthetic, so it may
    /// as well sound synthetic on purpose.</para>
    ///
    /// <para>Everything is mono at 44.1 kHz. The clips are short (under a second, mostly under
    /// 300 ms), so total generation is a few milliseconds and total memory is trivial.</para>
    /// </summary>
    public static class ProceduralSfx
    {
        public const int SampleRate = 44100;

        private enum Waveform { Sine, Square, Triangle, Saw }

        public static AudioClip Build(Sfx sfx)
        {
            switch (sfx)
            {
                case Sfx.UiTap: return BuildUiTap();
                case Sfx.Deploy: return BuildDeploy();
                case Sfx.SentryFire: return BuildSentryFire();
                case Sfx.UnitDeath: return BuildUnitDeath();
                case Sfx.BaseHit: return BuildBaseHit();
                case Sfx.Rally: return BuildRally();
                case Sfx.PhaseChange: return BuildPhaseChange();
                case Sfx.Victory: return BuildVictory();
                case Sfx.Defeat: return BuildDefeat();
                default: return null;
            }
        }

        // --- The individual sounds -------------------------------------------------------------
        //
        // Each one is a short additive recipe. The pitches are chosen to sit in a consistent
        // key (roughly A minor) so that sounds landing on top of each other during a busy siege
        // still agree with one another instead of clashing.

        /// <summary>Tiny neutral click for menu buttons. Must be short enough to feel instant.</summary>
        private static AudioClip BuildUiTap()
        {
            var buffer = NewBuffer(0.06f);
            AddTone(buffer, 0f, 0.05f, 880f, 660f, 0.25f, Waveform.Triangle, decay: 40f);
            AddNoise(buffer, 0f, 0.02f, 0.10f, decay: 90f, lowPassHz: 6000f);
            return Finish("sfx_ui_tap", buffer);
        }

        /// <summary>Unit deployed: a confident upward blip, so committing a resource feels positive.</summary>
        private static AudioClip BuildDeploy()
        {
            var buffer = NewBuffer(0.22f);
            AddTone(buffer, 0f, 0.18f, 330f, 660f, 0.30f, Waveform.Square, decay: 14f);
            AddTone(buffer, 0.02f, 0.16f, 495f, 990f, 0.12f, Waveform.Sine, decay: 16f);
            AddNoise(buffer, 0f, 0.03f, 0.08f, decay: 70f, lowPassHz: 4000f);
            return Finish("sfx_deploy", buffer);
        }

        /// <summary>
        /// Sentry shot. Fires often, so it is kept dry, quiet and very short - a repeated sound
        /// that rings is the fastest way to make a game unpleasant to listen to.
        /// </summary>
        private static AudioClip BuildSentryFire()
        {
            var buffer = NewBuffer(0.12f);
            AddNoise(buffer, 0f, 0.09f, 0.22f, decay: 55f, lowPassHz: 9000f);
            AddTone(buffer, 0f, 0.07f, 1200f, 300f, 0.16f, Waveform.Saw, decay: 45f);
            return Finish("sfx_sentry_fire", buffer);
        }

        /// <summary>Unit lost: a downward crunch. Reads as "that was yours" without being harsh.</summary>
        private static AudioClip BuildUnitDeath()
        {
            var buffer = NewBuffer(0.35f);
            AddTone(buffer, 0f, 0.30f, 240f, 70f, 0.28f, Waveform.Square, decay: 11f);
            AddNoise(buffer, 0f, 0.16f, 0.18f, decay: 22f, lowPassHz: 2200f);
            return Finish("sfx_unit_death", buffer);
        }

        /// <summary>
        /// A hit on a base. The heaviest sound in the game on purpose: base damage is the only
        /// thing that actually decides the match, so it should land harder than combat chatter.
        /// </summary>
        private static AudioClip BuildBaseHit()
        {
            var buffer = NewBuffer(0.45f);
            AddTone(buffer, 0f, 0.40f, 140f, 45f, 0.40f, Waveform.Sine, decay: 9f);
            AddTone(buffer, 0f, 0.12f, 220f, 110f, 0.18f, Waveform.Square, decay: 26f);
            AddNoise(buffer, 0f, 0.20f, 0.16f, decay: 18f, lowPassHz: 1400f);
            return Finish("sfx_base_hit", buffer);
        }

        /// <summary>
        /// Rally. This is the game's signature order - a board-wide command the player earns by
        /// physically standing back - so it gets the only fanfare-shaped sound in the set: a
        /// two-note horn call, deliberately longer and louder than everything else.
        /// </summary>
        private static AudioClip BuildRally()
        {
            var buffer = NewBuffer(0.75f);
            AddTone(buffer, 0f, 0.28f, 294f, 294f, 0.30f, Waveform.Saw, decay: 5f);
            AddTone(buffer, 0f, 0.28f, 147f, 147f, 0.20f, Waveform.Square, decay: 5f);
            AddTone(buffer, 0.24f, 0.48f, 440f, 440f, 0.32f, Waveform.Saw, decay: 4f);
            AddTone(buffer, 0.24f, 0.48f, 220f, 220f, 0.22f, Waveform.Square, decay: 4f);
            return Finish("sfx_rally", buffer);
        }

        /// <summary>Phase transition (Muster to Siege). A soft double blip - informative, not dramatic.</summary>
        private static AudioClip BuildPhaseChange()
        {
            var buffer = NewBuffer(0.36f);
            AddTone(buffer, 0f, 0.14f, 523f, 523f, 0.22f, Waveform.Triangle, decay: 16f);
            AddTone(buffer, 0.15f, 0.20f, 784f, 784f, 0.22f, Waveform.Triangle, decay: 14f);
            return Finish("sfx_phase_change", buffer);
        }

        /// <summary>Victory: a rising major arpeggio.</summary>
        private static AudioClip BuildVictory()
        {
            var buffer = NewBuffer(1.1f);
            AddTone(buffer, 0.00f, 0.30f, 440f, 440f, 0.26f, Waveform.Triangle, decay: 9f);
            AddTone(buffer, 0.16f, 0.30f, 554f, 554f, 0.26f, Waveform.Triangle, decay: 9f);
            AddTone(buffer, 0.32f, 0.30f, 659f, 659f, 0.26f, Waveform.Triangle, decay: 9f);
            AddTone(buffer, 0.48f, 0.60f, 880f, 880f, 0.30f, Waveform.Saw, decay: 4f);
            AddTone(buffer, 0.48f, 0.60f, 440f, 440f, 0.18f, Waveform.Square, decay: 4f);
            return Finish("sfx_victory", buffer);
        }

        /// <summary>Defeat: the same shape as victory, inverted and detuned flat.</summary>
        private static AudioClip BuildDefeat()
        {
            var buffer = NewBuffer(1.1f);
            AddTone(buffer, 0.00f, 0.34f, 415f, 415f, 0.26f, Waveform.Triangle, decay: 8f);
            AddTone(buffer, 0.20f, 0.34f, 349f, 349f, 0.26f, Waveform.Triangle, decay: 8f);
            AddTone(buffer, 0.42f, 0.65f, 262f, 247f, 0.30f, Waveform.Saw, decay: 4f);
            AddTone(buffer, 0.42f, 0.65f, 131f, 123f, 0.20f, Waveform.Square, decay: 4f);
            return Finish("sfx_defeat", buffer);
        }

        // --- Synthesis primitives --------------------------------------------------------------

        private static float[] NewBuffer(float seconds) => new float[Mathf.CeilToInt(seconds * SampleRate)];

        /// <summary>
        /// Adds an exponentially decaying oscillator, optionally sweeping between two pitches.
        /// Phase is accumulated per sample rather than computed from absolute time, which is what
        /// makes a sweep continuous instead of stepping and clicking at each sample.
        /// </summary>
        private static void AddTone(float[] buffer, float startSeconds, float durationSeconds,
            float startHz, float endHz, float amplitude, Waveform shape, float decay)
        {
            int start = Mathf.Clamp(Mathf.RoundToInt(startSeconds * SampleRate), 0, buffer.Length);
            int count = Mathf.Min(Mathf.RoundToInt(durationSeconds * SampleRate), buffer.Length - start);
            if (count <= 0) return;

            float phase = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / count;
                float hz = Mathf.Lerp(startHz, endHz, t);
                phase += hz / SampleRate;

                float envelope = Mathf.Exp(-decay * (i / (float)SampleRate)) * Attack(i);
                buffer[start + i] += Wave(shape, phase) * amplitude * envelope;
            }
        }

        /// <summary>
        /// Adds a decaying noise burst through a one-pole low-pass, which is what turns raw white
        /// noise (a hiss) into something that reads as an impact or a muzzle crack.
        /// </summary>
        private static void AddNoise(float[] buffer, float startSeconds, float durationSeconds,
            float amplitude, float decay, float lowPassHz)
        {
            int start = Mathf.Clamp(Mathf.RoundToInt(startSeconds * SampleRate), 0, buffer.Length);
            int count = Mathf.Min(Mathf.RoundToInt(durationSeconds * SampleRate), buffer.Length - start);
            if (count <= 0) return;

            // Standard one-pole coefficient: how much of the new sample survives per step.
            float alpha = Mathf.Clamp01(lowPassHz / (lowPassHz + SampleRate / (2f * Mathf.PI)));
            float previous = 0f;

            // A fixed seed keeps every run of the game sounding identical, so the sounds are a
            // designed asset rather than something that differs per launch.
            var random = new System.Random(unchecked(start * 73856093) ^ count);

            for (int i = 0; i < count; i++)
            {
                float white = (float)(random.NextDouble() * 2.0 - 1.0);
                previous += alpha * (white - previous);

                float envelope = Mathf.Exp(-decay * (i / (float)SampleRate)) * Attack(i);
                buffer[start + i] += previous * amplitude * envelope;
            }
        }

        /// <summary>
        /// A ~2 ms fade-in. Without it every sound starts at a non-zero sample, and that
        /// discontinuity is audible as a click on top of the intended sound.
        /// </summary>
        private static float Attack(int sampleIndex)
        {
            const int AttackSamples = SampleRate / 500;
            return sampleIndex >= AttackSamples ? 1f : sampleIndex / (float)AttackSamples;
        }

        private static float Wave(Waveform shape, float phase)
        {
            float wrapped = phase - Mathf.Floor(phase);
            switch (shape)
            {
                case Waveform.Sine: return Mathf.Sin(wrapped * 2f * Mathf.PI);
                case Waveform.Square: return wrapped < 0.5f ? 1f : -1f;
                case Waveform.Triangle: return 4f * Mathf.Abs(wrapped - 0.5f) - 1f;
                case Waveform.Saw: return 2f * wrapped - 1f;
                default: return 0f;
            }
        }

        /// <summary>
        /// Soft-clips through tanh and packs the buffer into an AudioClip. Layers are summed
        /// without headroom management above, so hard clipping is a real possibility - tanh keeps
        /// a hot mix sounding driven rather than broken.
        /// </summary>
        private static AudioClip Finish(string name, float[] buffer)
        {
            for (int i = 0; i < buffer.Length; i++)
                buffer[i] = (float)System.Math.Tanh(buffer[i] * 1.4f) * 0.85f;

            var clip = AudioClip.Create(name, buffer.Length, 1, SampleRate, false);
            clip.SetData(buffer, 0);
            return clip;
        }
    }
}
