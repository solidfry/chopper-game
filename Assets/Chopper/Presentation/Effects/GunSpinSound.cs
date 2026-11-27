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
        /// <summary>Seconds the fire loop fades in over the spin-up's end, and out under the spin-down's start.</summary>
        private const float FadeIn = 0.1f, FadeOut = 0.15f;

        private float _volume;
        private bool _fadingOut;

        private float _spin = -1f;
        private bool _rising;
        private SoundLoop _loop;
        private float _lastShot = float.NegativeInfinity;

        /// <summary>Call every frame with the gun's spin (0..1) and whether it fired this frame.</summary>
        public void Update(IAudioService audio, WeaponLook look, float spin, bool fired, float fireInterval, Vector3 position, float now)
        {
            if (audio == null || look == null) return;
            try { Step(audio, look, spin, fired, fireInterval, position, now); }
            finally { Mark(now); }
        }

        private void Step(IAudioService audio, WeaponLook look, float spin, bool fired, float fireInterval, Vector3 position, float now)
        {
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
            if (firing && (_loop == null || _fadingOut))
            {
                if (_loop == null)
                {
                    _loop = audio.StartLoop(look.FireLoop, position);
                    _volume = 0f;
                }
                _fadingOut = false;
            }
            if (!firing && _loop != null && !_fadingOut)
            {
                _fadingOut = true;
                if (look.TailCue != null) audio.Play(look.TailCue, position);
            }
            if (_loop == null) return;
            // First frame or a pause: a normal frame, not a jump.
            var dt = Mathf.Clamp(now - _lastFrame, 0f, 0.05f);
            _volume = _fadingOut ? _volume - dt / FadeOut : _volume + dt / FadeIn;
            _volume = Mathf.Clamp01(_volume);
            _loop.Set(position, _volume);
            if (_fadingOut && _volume <= 0f) Drop(audio);
        }

        private float _lastFrame;

        /// <summary>Call at the end of each frame's update (keeps the fade clock).</summary>
        private void Mark(float now) => _lastFrame = now;

        public void Stop(IAudioService audio, WeaponLook look, Vector3 position)
        {
            if (_loop == null) return;
            audio?.StopLoop(_loop);
            _loop = null;
            _fadingOut = false;
            if (look != null && look.TailCue != null) audio?.Play(look.TailCue, position);
        }

        /// <summary>Stops the loop with no tail (the gun is gone).</summary>
        public void Drop(IAudioService audio)
        {
            if (_loop != null) audio?.StopLoop(_loop);
            _loop = null;
            _fadingOut = false;
        }
    }
}
