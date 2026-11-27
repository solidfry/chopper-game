using Chopper.Presentation.Audio;
using UnityEngine;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// One gun's spin and continuous fire sound (owner 27 Nov: a minigun whines up, tears in one long burst, whines
    /// down). Fed the barrel spin and the shots each frame; shared by <see cref="GunEffectsSystem"/> and the weapon review
    /// scene. Plain class.
    /// </summary>
    public sealed class GunSpinSound
    {
        /// <summary>The fire loop stops this long after the last shot was due.</summary>
        private const float LoopGrace = 0.06f;

        private float _spin = -1f;
        private bool _rising;
        private SoundLoop _loop;
        private float _lastShot = float.NegativeInfinity;

        /// <summary>Call every frame with the gun's spin (0..1) and whether it fired this frame.</summary>
        public void Update(IAudioService audio, WeaponLook look, float spin, bool fired, float fireInterval, Vector3 position, float now)
        {
            if (audio == null || look == null) return;
            // Spin sounds: on the turn from still or falling to rising, and from full or rising to falling.
            if (_spin >= 0f)
            {
                if (spin > _spin + 1e-4f && !_rising)
                {
                    _rising = true;
                    if (look.SpinUpCue != null) audio.Play(look.SpinUpCue, position);
                }
                else if (spin < _spin - 1e-4f && _rising)
                {
                    _rising = false;
                    if (look.SpinDownCue != null) audio.Play(look.SpinDownCue, position);
                }
                else if (spin >= 1f && _spin < 1f) _rising = true;
            }
            _spin = spin;

            if (look.FireLoop == null) return;
            if (fired) _lastShot = now;
            var firing = now - _lastShot <= fireInterval + LoopGrace;
            if (firing && _loop == null) _loop = audio.StartLoop(look.FireLoop, position);
            if (_loop != null) _loop.Set(position);
            if (!firing && _loop != null) Stop(audio, look, position);
        }

        public void Stop(IAudioService audio, WeaponLook look, Vector3 position)
        {
            if (_loop == null) return;
            audio?.StopLoop(_loop);
            _loop = null;
            if (look != null && look.TailCue != null) audio?.Play(look.TailCue, position);
        }

        /// <summary>Stops the loop with no tail (the gun is gone).</summary>
        public void Drop(IAudioService audio)
        {
            if (_loop != null) audio?.StopLoop(_loop);
            _loop = null;
        }
    }
}
