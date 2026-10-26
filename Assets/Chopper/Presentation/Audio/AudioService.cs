using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Chopper.Presentation.Audio
{
    /// <summary>
    /// <see cref="IAudioService"/> with a fixed pool of one-shot voices (oldest is replaced when all are busy), a pool of
    /// loop voices, two music sources for cross-fades, and mixer volumes saved in PlayerPrefs.
    /// Call <see cref="Tick"/> once per frame.
    /// </summary>
    public sealed class AudioService : IAudioService, IDisposable
    {
        private const string VolumeKeyPrefix = "chopper.volume.";
        private const float MinDecibels = -80f;

        private sealed class Voice
        {
            public AudioSource Source;
            public SoundCue Cue;
            public float StartTime;
        }

        private readonly SoundLibrary _library;
        private readonly Transform _root;
        private readonly Voice[] _voices;
        private readonly Stack<SoundLoop> _freeLoops = new();
        private readonly List<SoundLoop> _activeLoops = new();
        private readonly Dictionary<SoundCue, float> _lastPlayed = new();
        private readonly AudioSource[] _music = new AudioSource[2];
        private readonly float[] _musicTarget = new float[2];
        private readonly float[] _musicFadeSpeed = new float[2];
        private int _musicCurrent;

        public AudioService(SoundLibrary library, Transform root, int voiceCount = 32)
        {
            _library = library;
            _root = root;
            _voices = new Voice[voiceCount];
            for (var i = 0; i < voiceCount; i++)
                _voices[i] = new Voice { Source = CreateSource($"Voice {i}") };

            for (var i = 0; i < _music.Length; i++)
            {
                var source = CreateSource($"Music {i}");
                source.loop = true;
                source.spatialBlend = 0f;
                source.volume = 0f;
                source.outputAudioMixerGroup = library != null ? library.MusicGroup : null;
                _music[i] = source;
            }
        }

        public void Play(SoundCue cue, Vector3 position) => PlayAt(cue, position, atListener: false);

        public void Play(SoundCue cue) => PlayAt(cue, Vector3.zero, atListener: true);

        public SoundLoop StartLoop(SoundCue cue, Vector3 position)
        {
            if (cue == null || !cue.HasClips) return null;

            var loop = _freeLoops.Count > 0 ? _freeLoops.Pop() : new SoundLoop { Source = CreateSource("Loop") };
            var source = loop.Source;
            cue.Apply(source);
            source.clip = cue.PickClip();
            source.loop = true;
            loop.BaseVolume = cue.PickVolume();
            loop.BasePitch = cue.PickPitch();
            loop.Set(position);
            // Start at a random point, so many choppers do not play the same loop in step.
            source.time = source.clip.length * UnityEngine.Random.value;
            source.Play();
            _activeLoops.Add(loop);
            return loop;
        }

        public void StopLoop(SoundLoop loop)
        {
            if (loop == null || !_activeLoops.Remove(loop)) return;
            if (loop.Source != null) loop.Source.Stop();
            _freeLoops.Push(loop);
        }

        public void PlayMusic(AudioClip clip, float fadeSeconds = 1.5f)
        {
            if (clip == null) return;
            var current = _music[_musicCurrent];
            if (current.clip == clip && _musicTarget[_musicCurrent] > 0f) return;

            FadeMusic(_musicCurrent, 0f, fadeSeconds);
            _musicCurrent = 1 - _musicCurrent;
            var next = _music[_musicCurrent];
            next.clip = clip;
            next.volume = 0f;
            next.Play();
            FadeMusic(_musicCurrent, 1f, fadeSeconds);
        }

        public void StopMusic(float fadeSeconds = 1.5f)
        {
            FadeMusic(0, 0f, fadeSeconds);
            FadeMusic(1, 0f, fadeSeconds);
        }

        public float GetVolume(AudioChannel channel) => PlayerPrefs.GetFloat(VolumeKeyPrefix + channel, 1f);

        public void SetVolume(AudioChannel channel, float volume)
        {
            volume = Mathf.Clamp01(volume);
            PlayerPrefs.SetFloat(VolumeKeyPrefix + channel, volume);
            ApplyVolume(channel, volume);
        }

        /// <summary>
        /// Sets the mixer from the saved volumes. Call from Start or later: the mixer ignores SetFloat during Awake.
        /// </summary>
        public void ApplySavedVolumes()
        {
            foreach (AudioChannel channel in Enum.GetValues(typeof(AudioChannel)))
                ApplyVolume(channel, GetVolume(channel));
        }

        public void Tick(float deltaTime)
        {
            for (var i = 0; i < _music.Length; i++)
            {
                var source = _music[i];
                if (source == null || Mathf.Approximately(source.volume, _musicTarget[i])) continue;
                source.volume = Mathf.MoveTowards(source.volume, _musicTarget[i], _musicFadeSpeed[i] * deltaTime);
                if (source.volume <= 0f && _musicTarget[i] <= 0f)
                    source.Stop();
            }
        }

        public void Dispose()
        {
            foreach (var voice in _voices)
                if (voice.Source != null) Object.Destroy(voice.Source.gameObject);
            foreach (var loop in _activeLoops)
                if (loop.Source != null) Object.Destroy(loop.Source.gameObject);
            foreach (var loop in _freeLoops)
                if (loop.Source != null) Object.Destroy(loop.Source.gameObject);
            foreach (var source in _music)
                if (source != null) Object.Destroy(source.gameObject);
            _activeLoops.Clear();
            _freeLoops.Clear();
        }

        /// <summary>Linear 0..1 to mixer decibels.</summary>
        public static float ToDecibels(float volume) => volume <= 0.0001f ? MinDecibels : Mathf.Log10(volume) * 20f;

        private void PlayAt(SoundCue cue, Vector3 position, bool atListener)
        {
            if (cue == null || !cue.HasClips) return;

            var now = Time.unscaledTime;
            if (cue.Cooldown > 0f && _lastPlayed.TryGetValue(cue, out var last) && now - last < cue.Cooldown) return;
            _lastPlayed[cue] = now;

            var voice = PickVoice(cue);
            var source = voice.Source;
            cue.Apply(source);
            // 2D: position does not matter.
            if (atListener)
                source.spatialBlend = 0f;
            else
                source.transform.position = position;
            source.clip = cue.PickClip();
            source.volume = cue.PickVolume();
            source.pitch = cue.PickPitch();
            source.loop = false;
            source.Play();
            voice.Cue = cue;
            voice.StartTime = now;
        }

        /// <summary>
        /// A free voice; else, if the cue is at its voice limit, its oldest copy; else the oldest voice of all.
        /// </summary>
        private Voice PickVoice(SoundCue cue)
        {
            Voice free = null, oldest = null, oldestOfCue = null;
            var copies = 0;
            foreach (var voice in _voices)
            {
                var playing = voice.Source.isPlaying;
                if (!playing)
                {
                    free ??= voice;
                    continue;
                }
                if (oldest == null || voice.StartTime < oldest.StartTime) oldest = voice;
                if (voice.Cue != cue) continue;
                copies++;
                if (oldestOfCue == null || voice.StartTime < oldestOfCue.StartTime) oldestOfCue = voice;
            }

            if (copies >= cue.MaxVoices) return oldestOfCue;
            return free ?? oldest;
        }

        private void FadeMusic(int index, float target, float seconds)
        {
            _musicTarget[index] = target;
            _musicFadeSpeed[index] = seconds > 0f ? 1f / seconds : float.MaxValue;
        }

        private void ApplyVolume(AudioChannel channel, float volume)
        {
            var mixer = _library != null ? _library.Mixer : null;
            if (mixer != null && !mixer.SetFloat(channel + "Volume", ToDecibels(volume)))
                Debug.LogWarning($"Audio mixer '{mixer.name}' has no exposed parameter '{channel}Volume'.");
        }

        private AudioSource CreateSource(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            return source;
        }
    }
}
