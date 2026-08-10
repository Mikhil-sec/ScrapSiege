using System.Collections.Generic;
using UnityEngine;

namespace ScrapSiege.Audio
{
    /// <summary>
    /// The one entry point for sound: <c>GameAudio.Play(Sfx.Deploy)</c> from anywhere, at any time.
    ///
    /// <para>Bootstraps itself via <see cref="RuntimeInitializeOnLoadMethod"/> rather than being a
    /// prefab dropped into each scene. Both scenes would otherwise need the object wired in by
    /// hand, and a missing reference would silence the game in exactly the build where nobody
    /// checks. Self-creation means audio cannot be forgotten, and callers never need a serialized
    /// field or a null check.</para>
    ///
    /// <para>Sound is 2D. The board is a ~60 cm tabletop viewed through a phone held at arm's
    /// length, so every source is effectively equidistant - 3D panning would spend CPU and
    /// <see cref="ScrapSiege.Core.WorldScale"/> care to produce an effect nobody could hear.</para>
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public class GameAudio : MonoBehaviour
    {
        /// <summary>
        /// Voices available for simultaneous playback. A busy siege can land several unit deaths
        /// and sentry shots in the same frame; with one source they would cut each other off.
        /// </summary>
        private const int VoiceCount = 8;

        private const string MutePreferenceKey = "ScrapSiege.Audio.Muted";

        private static GameAudio instance;

        private readonly Dictionary<Sfx, AudioClip> clips = new Dictionary<Sfx, AudioClip>();
        private AudioSource[] voices;
        private int nextVoice;

        /// <summary>
        /// Per-sound cooldowns. Sentry fire and unit deaths can retrigger many times per second,
        /// and the same short clip restarted every few milliseconds turns into a buzz instead of
        /// a series of events. Sounds tied to a single decisive moment are left uncapped.
        /// </summary>
        private static readonly Dictionary<Sfx, float> MinimumInterval = new Dictionary<Sfx, float>
        {
            { Sfx.SentryFire, 0.07f },
            { Sfx.UnitDeath, 0.06f },
            { Sfx.BaseHit, 0.05f },
            { Sfx.Deploy, 0.04f },
        };

        private readonly Dictionary<Sfx, float> lastPlayedAt = new Dictionary<Sfx, float>();

        // Mirrors the PlayerPrefs value so the hot path (every sound played) is a field read
        // rather than a preferences lookup. Loaded once in Awake.
        private static bool muted;

        public static bool Muted
        {
            get => muted;
            set
            {
                muted = value;
                PlayerPrefs.SetInt(MutePreferenceKey, value ? 1 : 0);
                PlayerPrefs.Save();
                if (instance != null) instance.ApplyMute();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;

            var host = new GameObject("GameAudio");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<GameAudio>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            muted = PlayerPrefs.GetInt(MutePreferenceKey, 0) == 1;

            voices = new AudioSource[VoiceCount];
            for (int i = 0; i < VoiceCount; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f; // 2D - see the class summary.
                voices[i] = source;
            }

            // Generated once up front rather than on first use: synthesis is only a few
            // milliseconds in total, but doing it lazily would put that cost on the first shot
            // fired, which is precisely the moment a hitch is most visible.
            foreach (Sfx sfx in System.Enum.GetValues(typeof(Sfx)))
            {
                var clip = ProceduralSfx.Build(sfx);
                if (clip != null) clips[sfx] = clip;
            }

            ApplyMute();
        }

        /// <summary>Plays a sound. Safe to call before the bootstrap has run and safe to spam.</summary>
        public static void Play(Sfx sfx, float volumeScale = 1f)
        {
            if (instance == null)
            {
                // Only happens if something plays audio from a static constructor or an editor
                // context that runs before BeforeSceneLoad. Creating it on demand is cheaper than
                // making every caller defensive.
                Bootstrap();
                if (instance == null) return;
            }

            instance.PlayInternal(sfx, volumeScale);
        }

        private void PlayInternal(Sfx sfx, float volumeScale)
        {
            if (Muted) return;
            if (!clips.TryGetValue(sfx, out var clip) || clip == null) return;

            if (MinimumInterval.TryGetValue(sfx, out float interval))
            {
                if (lastPlayedAt.TryGetValue(sfx, out float last) && Time.unscaledTime - last < interval)
                    return;
                lastPlayedAt[sfx] = Time.unscaledTime;
            }

            // Round-robin rather than "find a free voice": when everything is busy the oldest
            // voice is the right one to steal, and round-robin approximates that for free.
            var source = voices[nextVoice];
            nextVoice = (nextVoice + 1) % VoiceCount;

            // Volume rides on PlayOneShot only - also setting source.volume would square it.
            source.PlayOneShot(clip, Mathf.Clamp01(volumeScale));
        }

        private void ApplyMute()
        {
            if (voices == null) return;
            bool muted = Muted;
            foreach (var source in voices)
                if (source != null) source.mute = muted;
        }

        /// <summary>Wire to a mute toggle button. Returns the new state so UI can relabel itself.</summary>
        public static bool ToggleMute()
        {
            Muted = !Muted;
            return Muted;
        }
    }
}
