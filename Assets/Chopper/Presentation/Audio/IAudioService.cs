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
        internal CueMuffle Muffle;
        internal AudioLowPassFilter Filter;
        internal float VolumeScale = 1f;
        /// <summary>0 = clear line to the listener, 1 = a building in the way (<see cref="IAudioPlace"/>).</summary>
        internal float Occlusion;
        internal bool Occluded;

        public bool IsPlaying => Source != null && Source.isPlaying;

        /// <param name="volumeScale">Multiplies the cue's volume.</param>
        /// <param name="pitchScale">Multiplies the cue's pitch.</param>
        public void Set(Vector3 position, float volumeScale = 1f, float pitchScale = 1f)
        {
            if (Source == null) return;
            Source.transform.position = position;
            VolumeScale = volumeScale;
            Source.volume = BaseVolume * volumeScale * AudioDistance.OcclusionGain(Occlusion);
            Source.pitch = BasePitch * pitchScale;
        }
    }

    /// <summary>All game audio goes through this. No AudioSources in gameplay code.</summary>
    public interface IAudioService
    {
        /// <summary>One-shot at a world position (3D as far as the cue's spatial blend says). Null or empty cues do nothing.</summary>
        void Play(SoundCue cue, Vector3 position);

        /// <summary>
        /// One-shot at a world position, quieter or louder than the cue (<paramref name="volumeScale"/> multiplies its
        /// volume; crashes scale with how hard the hit was).
        /// </summary>
        void Play(SoundCue cue, Vector3 position, float volumeScale);

        /// <summary>One-shot at the listener (UI, own feedback).</summary>
        void Play(SoundCue cue);

        /// <summary>
        /// One-shot at the listener with its pitch scaled (<paramref name="pitchScale"/> multiplies the cue's pitch): a
        /// count-up that rises as it fills.
        /// </summary>
        void PlayPitched(SoundCue cue, float pitchScale, float volumeScale = 1f);

        /// <summary>
        /// A voice line at the listener (pilot call signs, radio calls). A new line cuts the one playing, so calls never
        /// pile up. Null does nothing.
        /// </summary>
        void PlayVoice(AudioClip clip, float volume = 1f);

        /// <summary>The voice line playing now; null when none plays.</summary>
        AudioClip VoiceClip { get; }

        /// <summary>Loudness (RMS, 0..1) of the voice line now: the pilot portrait's jaw follows it. 0 when none plays.</summary>
        float VoiceLevel { get; }

        /// <summary>Returns null for a null or empty cue.</summary>
        SoundLoop StartLoop(SoundCue cue, Vector3 position);

        void StopLoop(SoundLoop loop);

        /// <summary>Cross-fades to <paramref name="clip"/> (looping). Same clip again does nothing. Ends a playlist.</summary>
        void PlayMusic(AudioClip clip, float fadeSeconds = 1.5f);

        /// <summary>The music clip playing (or fading in) now; null when none.</summary>
        AudioClip CurrentMusic { get; }

        /// <summary>
        /// Plays the clips shuffled, each to its end, cross-fading <paramref name="fadeSeconds"/> into the next; shuffles
        /// again after the last. One clip = it loops.
        /// </summary>
        void PlayPlaylist(IReadOnlyList<AudioClip> clips, float fadeSeconds = 3f);

        void StopMusic(float fadeSeconds = 1.5f);

        /// <summary>
        /// Music level (0..1) on top of the player's music volume, reached over <paramref name="seconds"/>. Matches turn
        /// it down (<see cref="MusicLibrary"/> levels) so the world comes through.
        /// </summary>
        void SetMusicLevel(float level, float seconds);

        /// <summary>0..1, saved for the next session.</summary>
        float GetVolume(AudioChannel channel);

        void SetVolume(AudioChannel channel, float volume);
    }

    /// <summary>A playing 3D sound the place probe can test for a building in the way.</summary>
    public readonly struct AudioEmitter
    {
        public readonly int Id;
        public readonly Vector3 Position;
        public AudioEmitter(int id, Vector3 position)
        {
            Id = id;
            Position = position;
        }
    }

    /// <summary>
    /// What the world round the listener does to sound, measured with physics rays by the client
    /// <c>AudioPlaceProbeSystem</c>: how closed in the listener is (street echo) and which sounds a building hides.
    /// </summary>
    public interface IAudioPlace
    {
        /// <summary>The listener's position (the camera's audio listener).</summary>
        Vector3 Listener { get; }

        /// <param name="enclosure">0 = open sky, 1 = close walls all round.</param>
        /// <param name="wallDistance">Mean distance to the walls that were hit (m).</param>
        void SetEnclosure(float enclosure, float wallDistance);

        /// <summary>Fills <paramref name="into"/> with the playing 3D sounds. Ids are valid until the next call.</summary>
        void Emitters(List<AudioEmitter> into);

        /// <summary>A building is (or is not) between this sound and the listener. Changes fade in.</summary>
        void SetOccluded(int id, bool occluded);
    }
}
