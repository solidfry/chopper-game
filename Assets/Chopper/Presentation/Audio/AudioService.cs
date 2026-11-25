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
        private float _cabinCutoff = OpenCutoff;
        private AudioSpace _space;
        private AudioReverbZone _zone;
        private AudioListener _listener;
        private float _nextListenerSearch;

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

        public void PlayPitched(SoundCue cue, float pitchScale, float volumeScale = 1f) =>
            PlayAt(cue, Vector3.zero, atListener: true, Mathf.Max(0f, volumeScale), Mathf.Clamp(pitchScale, 0.1f, 3f));

        public void PlayVoice(AudioClip clip, float volume = 1f)
        {
            if (clip == null) return;
            if (_voice == null)
            {
                _voice = CreateSource("Voice");
                _voice.spatialBlend = 0f;
                _voice.reverbZoneMix = 0f;
                _voice.outputAudioMixerGroup = _library != null ? _library.VoiceGroup : null;
            }
            _voice.Stop();
            _voice.clip = clip;
            _voice.volume = Mathf.Clamp01(volume);
            _voice.Play();
        }

        public AudioClip VoiceClip => _voice != null && _voice.isPlaying ? _voice.clip : null;

        public float VoiceLevel
        {
            get
            {
                if (_voice == null || !_voice.isPlaying) return 0f;
                _voice.GetOutputData(_voiceSamples, 0);
                var sum = 0f;
                foreach (var sample in _voiceSamples) sum += sample * sample;
                return Mathf.Sqrt(sum / _voiceSamples.Length);
            }
        }

        /// <summary>About 6 ms at 44.1 kHz: short enough for syllables.</summary>
        private readonly float[] _voiceSamples = new float[256];

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
            loop.Muffle = cue.Muffle;
            loop.Set(position);
            Shape(source, ref loop.Filter, cue.Muffle, ListenerPosition());
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

        /// <summary>Low-pass (Hz) for each kind of <see cref="CueMuffle"/>, on playing sounds too.</summary>
        public void SetCutoffs(float outside, float cabin)
        {
            outside = Mathf.Clamp(outside, 100f, OpenCutoff);
            cabin = Mathf.Clamp(cabin, 100f, OpenCutoff);
            if (Mathf.Approximately(outside, _outsideCutoff) && Mathf.Approximately(cabin, _cabinCutoff)) return;
            _outsideCutoff = outside;
            _cabinCutoff = cabin;
            // Playing sounds take the new cutoffs on the next Tick.
        }

        /// <summary>
        /// The place the listener is in (<see cref="AudioSpace"/>): its reverb on every 3D sound, and how distance dulls
        /// and wets them. Null = no reverb, no distance treatment.
        /// </summary>
        public void SetSpace(AudioSpace space)
        {
            _space = space;
            if (space == null)
            {
                if (_zone != null) _zone.enabled = false;
                return;
            }
            if (_zone == null)
            {
                var go = new GameObject("Reverb");
                go.transform.SetParent(_root, false);
                _zone = go.AddComponent<AudioReverbZone>();
                // Huge: the listener is always inside it, wherever it flies.
                _zone.minDistance = 100000f;
                _zone.maxDistance = 100001f;
            }
            space.Apply(_zone);
            _zone.enabled = true;
        }

        public AudioSpace Space => _space;

        private float Cutoff(CueMuffle muffle) => muffle == CueMuffle.Cabin ? _cabinCutoff : _outsideCutoff;

        public void Tick(float deltaTime)
        {
            if (_ownsMusic) _music.Tick(deltaTime);
            var listener = ListenerPosition();
            if (_zone != null) _zone.transform.position = listener;
            // Sounds move and so does the listener: distance treatment and cockpit muffling on every playing sound.
            foreach (var voice in _voices)
                if (voice.Cue != null && voice.Source.isPlaying) Shape(voice.Source, ref voice.Filter, voice.Cue.Muffle, listener);
            foreach (var loop in _activeLoops)
                if (loop.Source != null) Shape(loop.Source, ref loop.Filter, loop.Muffle, listener);
        }

        private Vector3 ListenerPosition()
        {
            if (_listener == null && Time.unscaledTime >= _nextListenerSearch)
            {
                _listener = Object.FindAnyObjectByType<AudioListener>();
                _nextListenerSearch = Time.unscaledTime + 1f;
            }
            return _listener != null ? _listener.transform.position : Vector3.zero;
        }

        public void Dispose()
        {
            if (_voice != null) Object.Destroy(_voice.gameObject);
            if (_zone != null) Object.Destroy(_zone.gameObject);
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

        private void PlayAt(SoundCue cue, Vector3 position, bool atListener, float volumeScale = 1f, float pitchScale = 1f)
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
            Shape(source, ref voice.Filter, cue.Muffle, ListenerPosition());
            source.clip = cue.PickClip();
            source.volume = cue.PickVolume() * volumeScale;
            source.pitch = cue.PickPitch() * pitchScale;
            source.loop = false;
            source.Play();
            voice.Cue = cue;
            voice.StartTime = now;
        }

        /// <summary>
        /// Low-pass and echo amount for one sound: the cockpit muffle (<see cref="CueMuffle"/>), and for 3D sounds the
        /// place's distance rules (<see cref="AudioSpace"/>: far = duller, more echo). 2D sounds (UI, own cockpit) get no
        /// echo. Pooled sources are reused: the filter is added the first time it is needed and is off when open.
        /// </summary>
        private void Shape(AudioSource source, ref AudioLowPassFilter filter, CueMuffle muffle, Vector3 listener)
        {
            var cutoff = muffle == CueMuffle.None ? OpenCutoff : Cutoff(muffle);
            var mix = 0f;
            var blend = source.spatialBlend;
            if (_space != null && blend > 0f)
            {
                var distance = Vector3.Distance(source.transform.position, listener);
                var air = AudioDistance.AirCutoff(distance, _space.NearDistance, _space.FarDistance, _space.FarCutoff);
                cutoff = Mathf.Min(cutoff, Mathf.Lerp(OpenCutoff, air, blend));
                mix = blend * AudioDistance.ReverbMix(distance, _space.NearDistance, _space.FarDistance, _space.NearReverbMix, _space.FarReverbMix);
            }
            source.reverbZoneMix = mix;
            if (cutoff >= OpenCutoff - 1f)
            {
                if (filter != null) filter.enabled = false;
                return;
            }
            if (filter == null)
            {
                filter = source.gameObject.AddComponent<AudioLowPassFilter>();
                filter.lowpassResonanceQ = 1f;
            }
            filter.cutoffFrequency = cutoff;
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
