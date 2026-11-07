using System;
using UnityEngine;
using UnityEngine.Audio;

namespace Chopper.Presentation.Audio
{
    /// <summary>
    /// The player's channel volumes (saved in PlayerPrefs) on the mixer's exposed <c>&lt;Channel&gt;Volume</c>
    /// parameters, and a world fade (0..1) on the Effects and Ambient channels: the scene flow fades the world out while
    /// a scene changes and back in after, so sounds do not cut off. Music and UI do not fade with the world.
    /// One per game when the scene flow runs (it lives across scenes), else one per scene's <see cref="AudioService"/>.
    /// </summary>
    public sealed class MixerVolumes
    {
        private const string VolumeKeyPrefix = "chopper.volume.";
        private const float MinDecibels = -80f;

        private AudioMixer _mixer;
        private float _worldFade = 1f;

        public AudioMixer Mixer => _mixer;

        /// <summary>The mixer to drive. Applies the saved volumes when it changes.</summary>
        public void UseMixer(AudioMixer mixer)
        {
            if (mixer == _mixer) return;
            _mixer = mixer;
            ApplyAll();
        }

        /// <summary>0..1 on Effects and Ambient (scene changes).</summary>
        public float WorldFade
        {
            get => _worldFade;
            set
            {
                value = Mathf.Clamp01(value);
                if (Mathf.Approximately(value, _worldFade)) return;
                _worldFade = value;
                Apply(AudioChannel.Effects);
                Apply(AudioChannel.Ambient);
            }
        }

        /// <summary>0..1 as the player set it, saved for the next session.</summary>
        public float Get(AudioChannel channel) => PlayerPrefs.GetFloat(VolumeKeyPrefix + channel, DefaultVolume(channel));

        public void Set(AudioChannel channel, float volume)
        {
            PlayerPrefs.SetFloat(VolumeKeyPrefix + channel, Mathf.Clamp01(volume));
            Apply(channel);
        }

        /// <summary>
        /// Sets the mixer from the saved volumes. Call from Start or later: the mixer ignores SetFloat during Awake.
        /// </summary>
        public void ApplyAll()
        {
            foreach (AudioChannel channel in Enum.GetValues(typeof(AudioChannel))) Apply(channel);
        }

        /// <summary>Linear 0..1 to mixer decibels.</summary>
        public static float ToDecibels(float volume) => volume <= 0.0001f ? MinDecibels : Mathf.Log10(volume) * 20f;

        /// <summary>Until the player sets it: music at half, so it sits under the effects (owner, 31 Oct).</summary>
        private static float DefaultVolume(AudioChannel channel) => channel == AudioChannel.Music ? 0.5f : 1f;

        private void Apply(AudioChannel channel)
        {
            if (_mixer == null) return;
            var volume = Get(channel);
            if (channel is AudioChannel.Effects or AudioChannel.Ambient) volume *= _worldFade;
            if (!_mixer.SetFloat(channel + "Volume", ToDecibels(volume)))
                Debug.LogWarning($"Audio mixer '{_mixer.name}' has no exposed parameter '{channel}Volume'.");
        }
    }
}
