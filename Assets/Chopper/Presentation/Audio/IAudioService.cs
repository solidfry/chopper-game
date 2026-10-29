using System.Collections.Generic;
using UnityEngine;

namespace Chopper.Presentation.Audio
{
    /// <summary>Mixer channels the player can set in the options.</summary>
    public enum AudioChannel
    {
        Master,
        Music,
        Effects,
        UI,
        Ambient,
    }

    /// <summary>
    /// A looping sound that follows something (rotor, alarm). Move it and change it every frame; stop it with
    /// <see cref="IAudioService.StopLoop"/>.
    /// </summary>
    public sealed class SoundLoop
    {
        internal AudioSource Source;
        internal float BaseVolume;
        internal float BasePitch;

        public bool IsPlaying => Source != null && Source.isPlaying;

        /// <param name="volumeScale">Multiplies the cue's volume.</param>
        /// <param name="pitchScale">Multiplies the cue's pitch.</param>
        public void Set(Vector3 position, float volumeScale = 1f, float pitchScale = 1f)
        {
            if (Source == null) return;
            Source.transform.position = position;
            Source.volume = BaseVolume * volumeScale;
            Source.pitch = BasePitch * pitchScale;
        }
    }

    /// <summary>All game audio goes through this. No AudioSources in gameplay code.</summary>
    public interface IAudioService
    {
        /// <summary>One-shot at a world position (3D as far as the cue's spatial blend says). Null or empty cues do nothing.</summary>
        void Play(SoundCue cue, Vector3 position);

        /// <summary>One-shot at the listener (UI, own feedback).</summary>
        void Play(SoundCue cue);

        /// <summary>Returns null for a null or empty cue.</summary>
        SoundLoop StartLoop(SoundCue cue, Vector3 position);

        void StopLoop(SoundLoop loop);

        /// <summary>Cross-fades to <paramref name="clip"/> (looping). Same clip again does nothing. Ends a playlist.</summary>
        void PlayMusic(AudioClip clip, float fadeSeconds = 1.5f);

        /// <summary>
        /// Plays the clips shuffled, each to its end, cross-fading <paramref name="fadeSeconds"/> into the next; shuffles
        /// again after the last. One clip = it loops.
        /// </summary>
        void PlayPlaylist(IReadOnlyList<AudioClip> clips, float fadeSeconds = 3f);

        void StopMusic(float fadeSeconds = 1.5f);

        /// <summary>0..1, saved for the next session.</summary>
        float GetVolume(AudioChannel channel);

        void SetVolume(AudioChannel channel, float volume);
    }
}
