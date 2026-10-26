using System.Collections.Generic;
using Chopper.Simulation.Config;
using UnityEngine;
using UnityEngine.Audio;

namespace Chopper.Presentation.Audio
{
    /// <summary>
    /// One sound the game can play: a few clip variations plus how to play them (volume, pitch, 2D/3D, mixer group,
    /// voice limit). Play it through <see cref="IAudioService"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Chopper/Audio/Sound Cue", fileName = "SoundCue")]
    public sealed class SoundCue : ScriptableObject, IValidatedAsset
    {
        [SerializeField, Tooltip("One is picked at random, never the same one twice in a row.")]
        private AudioClip[] clips = System.Array.Empty<AudioClip>();
        [SerializeField] private AudioMixerGroup output;

        [Header("Volume and pitch")]
        [SerializeField, Range(0f, 1f)] private float volume = 1f;
        [SerializeField, Range(0f, 1f), Tooltip("Random volume change, as a fraction (0.1 = up to 10% quieter).")]
        private float volumeVariation = 0.1f;
        [SerializeField, Range(0.1f, 3f)] private float pitch = 1f;
        [SerializeField, Range(0f, 1f), Tooltip("Random pitch change either way (0.05 = ±5%).")]
        private float pitchVariation = 0.05f;

        [Header("Space")]
        [SerializeField, Range(0f, 1f), Tooltip("0 = 2D (UI, own feedback), 1 = full 3D (world sounds).")]
        private float spatialBlend = 1f;
        [SerializeField] private float minDistance = 5f;
        [SerializeField] private float maxDistance = 400f;

        [Header("Limits")]
        [SerializeField, Tooltip("Most copies playing at once. A new one replaces the oldest.")]
        private int maxVoices = 8;
        [SerializeField, Tooltip("Least seconds between two plays. 0 = no limit.")]
        private float cooldown;

        [System.NonSerialized] private int _lastClip = -1;

        public AudioMixerGroup Output => output;
        public float SpatialBlend => spatialBlend;
        public float MinDistance => minDistance;
        public float MaxDistance => maxDistance;
        public int MaxVoices => Mathf.Max(1, maxVoices);
        public float Cooldown => cooldown;
        public bool HasClips => clips.Length > 0;

        public void CollectProblems(List<string> problems)
        {
            if (clips.Length == 0) problems.Add($"{name}: no clips.");
            foreach (var clip in clips)
                if (clip == null) { problems.Add($"{name}: empty clip entry."); break; }
            if (output == null) problems.Add($"{name}: no mixer group, so no volume setting covers it.");
        }

        public AudioClip PickClip()
        {
            if (clips.Length == 0) return null;
            if (clips.Length == 1) return clips[0];

            int index;
            if (_lastClip < 0 || _lastClip >= clips.Length)
            {
                index = Random.Range(0, clips.Length);
            }
            else
            {
                // Pick from all but the last one played.
                index = Random.Range(0, clips.Length - 1);
                if (index >= _lastClip) index++;
            }
            _lastClip = index;
            return clips[index];
        }

        public float PickVolume() => volume * (1f - Random.value * volumeVariation);
        public float PickPitch() => pitch * (1f + Random.Range(-pitchVariation, pitchVariation));

        /// <summary>Copies the cue's settings onto a source (clip, volume and pitch are set by the caller).</summary>
        public void Apply(AudioSource source)
        {
            source.outputAudioMixerGroup = output;
            source.spatialBlend = spatialBlend;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.dopplerLevel = spatialBlend > 0f ? 0.5f : 0f;
        }

        /// <summary>Editor only: used by the audio setup tool.</summary>
        public void Initialise(AudioClip[] clipList, AudioMixerGroup group, float cueVolume, float blend, int voices, float minimumGap = 0f,
            float cuePitch = 1f)
        {
            clips = clipList;
            output = group;
            volume = cueVolume;
            spatialBlend = blend;
            maxVoices = voices;
            cooldown = minimumGap;
            pitch = cuePitch;
        }
    }
}
