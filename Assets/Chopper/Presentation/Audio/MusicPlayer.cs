using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using Object = UnityEngine.Object;

namespace Chopper.Presentation.Audio
{
    /// <summary>
    /// Two music sources for cross-fades, a shuffled playlist, and a music level (0..1) on top of the player's music
    /// volume: matches turn the music down so the rotors and the guns come through (<see cref="MusicLibrary"/> levels).
    /// The scene flow (<see cref="Flow.SceneFlow"/>) owns one that lives across scene loads, so music cross-fades from
    /// one scene into the next; without it each scene's <see cref="AudioService"/> makes its own.
    /// Call <see cref="Tick"/> once per frame with unscaled time.
    /// </summary>
    public sealed class MusicPlayer : IDisposable
    {
        private readonly AudioSource[] _sources = new AudioSource[2];
        private readonly float[] _fade = new float[2];
        private readonly float[] _fadeTarget = new float[2];
        private readonly float[] _fadeSpeed = new float[2];
        private int _current;
        private readonly List<AudioClip> _playlist = new();
        private int _playlistIndex;
        private float _playlistFade;
        private float _level = 1f;
        private float _levelTarget = 1f;
        private float _levelSpeed = float.MaxValue;

        public MusicPlayer(Transform root)
        {
            for (var i = 0; i < _sources.Length; i++)
            {
                var go = new GameObject($"Music {i}");
                go.transform.SetParent(root, false);
                var source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = true;
                source.spatialBlend = 0f;
                source.volume = 0f;
                // Music keeps playing while the game is paused (options menu) and while a scene loads.
                source.ignoreListenerPause = true;
                _sources[i] = source;
            }
        }

        /// <summary>The music level now (0..1), before the player's music volume.</summary>
        public float Level => _level;

        /// <summary>The mixer group the music plays through (the library's music group). Null = straight to the listener.</summary>
        public void SetOutput(AudioMixerGroup group)
        {
            foreach (var source in _sources)
                if (source != null) source.outputAudioMixerGroup = group;
        }

        /// <summary>Cross-fades to <paramref name="clip"/> (looping). The same clip again does nothing. Ends a playlist.</summary>
        public void Play(AudioClip clip, float fadeSeconds)
        {
            if (clip == null) return;
            _playlist.Clear();
            if (_sources[_current].clip == clip && _fadeTarget[_current] > 0f) return;
            StartTrack(clip, fadeSeconds, loop: true);
        }

        /// <summary>
        /// Plays the clips shuffled, each to its end, cross-fading into the next; shuffles again after the last. One clip
        /// = it loops. The same set of clips while it already plays does nothing (a match scene after a match scene).
        /// </summary>
        public void PlayPlaylist(IReadOnlyList<AudioClip> clips, float fadeSeconds)
        {
            if (clips == null) return;
            if (IsPlaying(clips)) return;

            _playlist.Clear();
            foreach (var clip in clips)
                if (clip != null) _playlist.Add(clip);
            if (_playlist.Count == 0) return;
            if (_playlist.Count == 1)
            {
                Play(_playlist[0], fadeSeconds);
                return;
            }

            _playlistFade = Mathf.Max(0.1f, fadeSeconds);
            Shuffle(null);
            _playlistIndex = 0;
            StartTrack(_playlist[0], _playlistFade, loop: false);
        }

        public void Stop(float fadeSeconds)
        {
            _playlist.Clear();
            FadeTo(0, 0f, fadeSeconds);
            FadeTo(1, 0f, fadeSeconds);
        }

        /// <summary>Moves the music level to <paramref name="level"/> over <paramref name="seconds"/> (0 = at once).</summary>
        public void SetLevel(float level, float seconds)
        {
            _levelTarget = Mathf.Clamp01(level);
            _levelSpeed = seconds > 0f ? Mathf.Max(Mathf.Abs(_levelTarget - _level), 0.01f) / seconds : float.MaxValue;
        }

        public void Tick(float deltaTime)
        {
            AdvancePlaylist();
            _level = Mathf.MoveTowards(_level, _levelTarget, _levelSpeed * deltaTime);
            for (var i = 0; i < _sources.Length; i++)
            {
                var source = _sources[i];
                if (source == null) continue;
                _fade[i] = Mathf.MoveTowards(_fade[i], _fadeTarget[i], _fadeSpeed[i] * deltaTime);
                // Equal-power cross-fade: the sum does not dip in the middle.
                source.volume = Mathf.Sin(_fade[i] * Mathf.PI * 0.5f) * _level;
                if (_fade[i] <= 0f && _fadeTarget[i] <= 0f && source.isPlaying) source.Stop();
            }
        }

        public void Dispose()
        {
            foreach (var source in _sources)
                if (source != null) Object.Destroy(source.gameObject);
            _playlist.Clear();
        }

        /// <summary>True when the playlist holds exactly these clips (any order) and a track plays.</summary>
        private bool IsPlaying(IReadOnlyList<AudioClip> clips)
        {
            if (_playlist.Count == 0 || _fadeTarget[_current] <= 0f) return false;
            var count = 0;
            foreach (var clip in clips)
            {
                if (clip == null) continue;
                if (!_playlist.Contains(clip)) return false;
                count++;
            }
            return count == _playlist.Count;
        }

        private void StartTrack(AudioClip clip, float fadeSeconds, bool loop)
        {
            FadeTo(_current, 0f, fadeSeconds);
            _current = 1 - _current;
            var next = _sources[_current];
            next.clip = clip;
            next.loop = loop;
            _fade[_current] = 0f;
            next.volume = 0f;
            next.Play();
            FadeTo(_current, 1f, fadeSeconds);
        }

        /// <summary>Playlist: when the track is near its end, cross-fade to the next one (shuffle again after the last).</summary>
        private void AdvancePlaylist()
        {
            if (_playlist.Count < 2) return;
            var current = _sources[_current];
            if (current == null || current.clip == null) return;
            if (current.isPlaying && current.time < current.clip.length - _playlistFade) return;

            _playlistIndex++;
            if (_playlistIndex >= _playlist.Count)
            {
                // A new order, but never the track that just played first.
                Shuffle(current.clip);
                _playlistIndex = 0;
            }
            StartTrack(_playlist[_playlistIndex], _playlistFade, loop: false);
        }

        private void Shuffle(AudioClip notFirst)
        {
            for (var i = _playlist.Count - 1; i > 0; i--)
            {
                var j = UnityEngine.Random.Range(0, i + 1);
                (_playlist[i], _playlist[j]) = (_playlist[j], _playlist[i]);
            }
            if (notFirst != null && _playlist[0] == notFirst) (_playlist[0], _playlist[^1]) = (_playlist[^1], _playlist[0]);
        }

        private void FadeTo(int index, float target, float seconds)
        {
            _fadeTarget[index] = target;
            _fadeSpeed[index] = seconds > 0f ? 1f / seconds : float.MaxValue;
        }
    }
}
