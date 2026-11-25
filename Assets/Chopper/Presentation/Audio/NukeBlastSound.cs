using System.Collections.Generic;
using UnityEngine;

namespace Chopper.Presentation.Audio
{
    /// <summary>
    /// The nuke blast sound, shared by the match (<see cref="NukeAudioSystem"/>) and the effects bench: silent until the
    /// shock front reaches the listener, then the close roar and the far rumble mixed by distance (<see cref="NukeSound.BlastMix"/>).
    /// </summary>
    public sealed class NukeBlastSound
    {
        private readonly List<(float At, float Near, float Far)> _queued = new();

        /// <summary>A blast at <paramref name="position"/> now; heard when its front (<paramref name="speed"/> m/s) arrives.</summary>
        public void Add(Vector3 position, Vector3 listener, float speed, float now)
        {
            var distance = Vector3.Distance(listener, position);
            NukeSound.BlastMix(distance, out var near, out var far);
            _queued.Add((now + distance / Mathf.Max(speed, 1f), near, far));
        }

        public void Tick(IAudioService audio, SoundLibrary sounds, Vector3 listener, float now)
        {
            for (var i = _queued.Count - 1; i >= 0; i--)
            {
                var (at, near, far) = _queued[i];
                if (at > now) continue;
                _queued.RemoveAt(i);
                if (audio == null || sounds == null) continue;
                // Both cues are 2D: a nuke fills the sky, and the volume already follows the distance.
                if (near > 0.01f) audio.Play(sounds.NukeBlastNear, listener, near);
                if (far > 0.01f) audio.Play(sounds.NukeBlastFar, listener, far);
            }
        }

        public void Clear() => _queued.Clear();
    }
}
