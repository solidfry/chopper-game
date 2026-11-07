using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Chopper.Presentation.Audio
{
    /// <summary>
    /// <see cref="IAudioService"/> with a fixed pool of one-shot voices (oldest is replaced when all are busy) and a pool
    /// of loop voices. Music and mixer volumes go to a <see cref="MusicPlayer"/> and <see cref="MixerVolumes"/>: the
    /// scene flow's (they live across scenes, see <see cref="UseShared"/>), else this service's own.
    /// Call <see cref="Tick"/> once per frame.
    /// </summary>
    public sealed class AudioService : IAudioService, IDisposable
    {
        private sealed class Voice
        {
            public AudioSource Source;
            public SoundCue Cue;
            public float StartTime;
            public AudioLowPassFilter Filter;
        }

        /// <summary>A low-pass at this frequency (Hz) changes nothing that can be heard.</summary>
        public const float OpenCutoff = 22000f;

        private readonly SoundLibrary _library;
        private readonly Transform _root;
        private readonly Voice[] _voices;
        private readonly Stack<SoundLoop> _freeLoops = new();
        private readonly List<SoundLoop> _activeLoops = new();
        private readonly Dictionary<SoundCue, float> _lastPlayed = new();
        private MusicPlayer _music;
        private AudioSource _voice;
        private MixerVolumes _volumes;
        private bool _ownsMusic = true;
        private float _outsideCutoff = OpenCutoff;

        public AudioService(SoundLibrary library, Transform root, int voiceCount = 32)
        {
            _library = library;
            _root = root;
            _voices = new Voice[voiceCount];
            for (var i = 0; i < voiceCount; i++)
                _voices[i] = new Voice { Source = CreateSource($"Voice {i}") };

            _music = new MusicPlayer(root);
            _music.SetOutput(library != null ? library.MusicGroup : null);
            _volumes = new MixerVolumes();
        }

        /// <summary>
        /// Music and volumes from the scene flow, which outlive this scene. The own music player is dropped (call before
        /// any music starts, or the scene's music starts again on the shared player).
        /// </summary>
        public void UseShared(MusicPlayer music, MixerVolumes volumes)
        {
            if (music != null && music != _music)
            {
                if (_ownsMusic) _music.Dispose();
                _music = music;
                _ownsMusic = false;
                _music.SetOutput(_library != null ? _library.MusicGroup : null);
            }
            if (volumes != null) _volumes = volumes;
            _volumes.UseMixer(_library != null ? _library.Mixer : null);
        }

        public MusicPlayer Music => _music;

        public void Play(SoundCue cue, Vector3 position) => PlayAt(cue, position, atListener: false);

        public void Play(SoundCue cue, Vector3 position, float volumeScale) =>
            PlayAt(cue, position, atListener: false, Mathf.Max(0f, volumeScale));

        public void Play(SoundCue cue) => PlayAt(cue, Vector3.zero, atListener: true);

        public void PlayVoice(AudioClip clip, float volume = 1f)
        {
            if (clip == null) return;
            if (_voice == null)
            {
                _voice = CreateSource("Voice");
                _voice.spatialBlend = 0f;
                _voice.outputAudioMixerGroup = _library != null ? _library.VoiceGroup : null;
            }
            _voice.Stop();
            _voice.clip = clip;
            _voice.volume = Mathf.Clamp01(volume);
            _voice.Play();
        }

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
            loop.Outside = cue.Outside;
            Muffle(source, ref loop.Filter, cue.Outside);
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

        public void PlayMusic(AudioClip clip, float fadeSeconds = 1.5f) => _music.Play(clip, fadeSeconds);

        public void PlayPlaylist(IReadOnlyList<AudioClip> clips, float fadeSeconds = 3f) => _music.PlayPlaylist(clips, fadeSeconds);

        public void StopMusic(float fadeSeconds = 1.5f) => _music.Stop(fadeSeconds);

        public void SetMusicLevel(float level, float seconds) => _music.SetLevel(level, seconds);

        public float GetVolume(AudioChannel channel) => _volumes.Get(channel);

        public void SetVolume(AudioChannel channel, float volume) => _volumes.Set(channel, volume);

        /// <summary>
        /// Sets the mixer from the saved volumes. Call from Start or later: the mixer ignores SetFloat during Awake.
        /// </summary>
        public void ApplySavedVolumes()
        {
            _volumes.UseMixer(_library != null ? _library.Mixer : null);
            _volumes.ApplyAll();
        }

        /// <summary>Low-pass (Hz) on sounds from outside the chopper (<see cref="SoundCue.Outside"/>), playing ones too.</summary>
        public void SetOutsideCutoff(float hertz)
        {
            hertz = Mathf.Clamp(hertz, 100f, OpenCutoff);
            if (Mathf.Approximately(hertz, _outsideCutoff)) return;
            _outsideCutoff = hertz;
            foreach (var voice in _voices)
                if (voice.Filter != null && voice.Filter.enabled) voice.Filter.cutoffFrequency = hertz;
            foreach (var loop in _activeLoops)
                if (loop.Outside && loop.Filter != null) loop.Filter.cutoffFrequency = hertz;
        }

        public void Tick(float deltaTime)
        {
            if (_ownsMusic) _music.Tick(deltaTime);
        }

        public void Dispose()
        {
            if (_voice != null) Object.Destroy(_voice.gameObject);
            foreach (var voice in _voices)
                if (voice.Source != null) Object.Destroy(voice.Source.gameObject);
            foreach (var loop in _activeLoops)
                if (loop.Source != null) Object.Destroy(loop.Source.gameObject);
            foreach (var loop in _freeLoops)
                if (loop.Source != null) Object.Destroy(loop.Source.gameObject);
            if (_ownsMusic) _music.Dispose();
            _activeLoops.Clear();
            _freeLoops.Clear();
        }

        /// <summary>Linear 0..1 to mixer decibels.</summary>
        public static float ToDecibels(float volume) => MixerVolumes.ToDecibels(volume);

        private void PlayAt(SoundCue cue, Vector3 position, bool atListener, float volumeScale = 1f)
        {
            if (cue == null || !cue.HasClips) return;

            var now = Time.unscaledTime;
            if (cue.Cooldown > 0f && _lastPlayed.TryGetValue(cue, out var last) && now - last < cue.Cooldown) return;
            _lastPlayed[cue] = now;

            var voice = PickVoice(cue);
            var source = voice.Source;
            cue.Apply(source);
            Muffle(source, ref voice.Filter, cue.Outside);
            // 2D: position does not matter.
            if (atListener)
                source.spatialBlend = 0f;
            else
                source.transform.position = position;
            source.clip = cue.PickClip();
            source.volume = cue.PickVolume() * volumeScale;
            source.pitch = cue.PickPitch();
            source.loop = false;
            source.Play();
            voice.Cue = cue;
            voice.StartTime = now;
        }

        /// <summary>Pooled sources are reused: the filter is on only for outside sounds. It is added the first time it is needed.</summary>
        private void Muffle(AudioSource source, ref AudioLowPassFilter filter, bool outside)
        {
            if (!outside)
            {
                if (filter != null) filter.enabled = false;
                return;
            }
            if (filter == null)
            {
                filter = source.gameObject.AddComponent<AudioLowPassFilter>();
                filter.lowpassResonanceQ = 1f;
            }
            filter.cutoffFrequency = _outsideCutoff;
            filter.enabled = true;
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
