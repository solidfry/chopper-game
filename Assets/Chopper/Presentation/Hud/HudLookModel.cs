using System;

namespace Chopper.Presentation.Hud
{
    /// <summary>
    /// The gameplay-driven part of the HUD look: turns hits, low hull, locks, missiles and disruptions (EMP, hack, death)
    /// into glitch, flicker, colour split, noise and pulse values for the shader. Plain C#, so it can be tested.
    /// <see cref="HudLook"/> feeds it and sends the outputs to the GPU.
    /// </summary>
    public sealed class HudLookModel
    {
        /// <summary>New flicker and glitch-band samples per second (steady, not tied to frame rate).</summary>
        private const float SampleRate = 30f;
        /// <summary>With 'reduce effects': glitch, pulse and noise are scaled by this; flicker is off.</summary>
        public const float ReducedScale = 0.25f;
        /// <summary>Glitch adds this much noise at full strength.</summary>
        private const float GlitchNoise = 0.3f;
        /// <summary>Glitch adds this much flicker at full strength.</summary>
        private const float GlitchFlicker = 0.4f;

        private readonly Random _random;
        private HudLookProfile _profile;
        private HudLookReactions _reactions;
        private bool _reduceEffects;
        private float _impulse;
        private float _pulse;
        private float _disruption;
        private float _disruptionTime;
        private float _sampleTime;
        private float _flickerSample;

        public HudLookModel(int seed = 1984) => _random = new Random(seed);

        /// <summary>The hull is below the low-health line: the HUD flickers more.</summary>
        public bool LowHull { get; set; }

        /// <summary>0 = no missile, 1 = about to hit (<see cref="ChopperHudModel.MissileUrgency"/>).</summary>
        public float MissileUrgency { get; set; }

        /// <summary>0 = clean, 1 = full glitch (bands torn sideways, colour split).</summary>
        public float Glitch { get; private set; }
        /// <summary>HUD brightness multiplier: flicker below 1, lock pulse above 1.</summary>
        public float Brightness { get; private set; } = 1f;
        /// <summary>0 to 1: extra glow from the lock pulse.</summary>
        public float Pulse { get; private set; }
        /// <summary>Colour split at the screen edge, in HUD texture pixels.</summary>
        public float Chromatic { get; private set; }
        public float Noise { get; private set; }
        /// <summary>Changes <see cref="SampleRate"/> times a second while glitching, so the torn bands jump.</summary>
        public float GlitchSeed { get; private set; }

        public void Configure(in HudLookProfile profile, in HudLookReactions reactions, bool reduceEffects)
        {
            _profile = profile;
            _reactions = reactions;
            _reduceEffects = reduceEffects;
        }

        /// <summary>We took damage: a glitch that grows with the damage.</summary>
        public void OnDamage(float amount)
        {
            if (amount <= 0f || _reactions.FullGlitchDamage <= 0f) return;
            _impulse = Math.Min(1f, _impulse + amount / _reactions.FullGlitchDamage);
        }

        /// <summary>Our seeker locked a target.</summary>
        public void OnLockAcquired() => _pulse = 1f;

        /// <summary>
        /// Holds the glitch at least at <paramref name="strength"/> for <paramref name="seconds"/>, then lets it decay.
        /// For death, spawn, and later EMP and hack darts.
        /// </summary>
        public void Disrupt(float strength, float seconds)
        {
            _disruption = Math.Max(_disruption, Math.Clamp(strength, 0f, 1f));
            _disruptionTime = Math.Max(_disruptionTime, seconds);
        }

        public void Tick(float deltaTime)
        {
            _impulse = Math.Max(0f, _impulse - _reactions.GlitchDecay * deltaTime);
            _pulse = Math.Max(0f, _pulse - _reactions.PulseDecay * deltaTime);
            if (_disruptionTime > 0f)
                _disruptionTime = Math.Max(0f, _disruptionTime - deltaTime);
            else
                _disruption = Math.Max(0f, _disruption - _reactions.GlitchDecay * deltaTime);

            var scale = _reduceEffects ? ReducedScale : 1f;
            Glitch = Math.Max(_impulse, _disruption) * scale;
            Pulse = _pulse * _reactions.LockPulse * scale;
            Chromatic = _profile.Chromatic + Glitch * _reactions.GlitchChromatic;
            Noise = (_profile.Noise + Glitch * GlitchNoise + Math.Clamp(MissileUrgency, 0f, 1f) * _reactions.MissileNoise) * scale;

            _sampleTime -= deltaTime;
            if (_sampleTime <= 0f)
            {
                _sampleTime += 1f / SampleRate;
                if (_sampleTime <= 0f) _sampleTime = 1f / SampleRate;
                _flickerSample = (float)_random.NextDouble();
                if (Glitch > 0f) GlitchSeed = (float)_random.NextDouble() * 100f;
            }

            // Flicker is the photosensitivity risk: 'reduce effects' turns it off completely.
            var flicker = _reduceEffects ? 0f : _profile.Flicker + (LowHull ? _reactions.LowHullFlicker : 0f) + Glitch * GlitchFlicker;
            Brightness = (1f - Math.Clamp(flicker, 0f, 1f) * _flickerSample) * (1f + Pulse);
        }
    }
}
