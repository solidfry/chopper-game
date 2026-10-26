using System;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace Chopper.Presentation.Hud
{
    /// <summary>
    /// View model for the in-flight HUD. <see cref="LocalChopperHudSystem"/> writes it once per frame; UI Toolkit
    /// reads it through data binding. Raises change events only when a shown value changes, so bindings do no work
    /// on frames where nothing moved.
    /// </summary>
    public sealed class ChopperHudModel : INotifyBindablePropertyChanged
    {
        private const float MetresPerSecondToKilometresPerHour = 3.6f;
        private const float LowHealthFraction = 0.3f;
        /// <summary>How long a hit marker shows, seconds.</summary>
        public const float HitMarkerDuration = 0.15f;
        public const float KillMarkerDuration = 0.6f;
        public const float DamageIndicatorDuration = 1f;
        /// <summary>Most the flight frame moves from the centre, in pixels (the old HUD used 24).</summary>
        public const float MaxSway = 24f;
        /// <summary>Pixels of sway per degree per second of turn rate.</summary>
        private const float SwayPerDegreePerSecond = 0.2f;
        /// <summary>How fast the frame follows the target sway (1/s). Lower = more lag, more weight.</summary>
        private const float SwayResponse = 6f;

        private bool _hasChopper;
        private string _speedText = "0";
        private string _altitudeText = "0";
        private float _dashCharge = 1f;
        private bool _dashReady = true;
        private string _healthText = "0";
        private float _healthFraction = 1f;
        private bool _healthLow;
        private bool _aimVisible;
        private Vector2 _aimViewport = new(0.5f, 0.5f);
        private float _hitMarkerTime;
        private float _killMarkerTime;
        private bool _hitMarkerVisible;
        private bool _killMarkerVisible;
        private bool _outOfBounds;
        private float _damageIndicatorTime;
        private bool _damageIndicatorVisible;
        private float _damageDirectionDegrees;
        private float _rollDegrees;
        private Vector2 _sway;
        private Vector2 _swayTarget;
        private Vector2 _swaySmoothed;

        public event EventHandler<BindablePropertyChangedEventArgs> propertyChanged;

        /// <summary>False while the local player has no chopper (menus, before spawn, after death).</summary>
        [CreateProperty]
        public bool HasChopper
        {
            get => _hasChopper;
            private set => Set(ref _hasChopper, value, nameof(HasChopper));
        }

        /// <summary>Ground speed in km/h, whole numbers.</summary>
        [CreateProperty]
        public string SpeedText
        {
            get => _speedText;
            private set => Set(ref _speedText, value, nameof(SpeedText));
        }

        /// <summary>Height above ground in metres, whole numbers.</summary>
        [CreateProperty]
        public string AltitudeText
        {
            get => _altitudeText;
            private set => Set(ref _altitudeText, value, nameof(AltitudeText));
        }

        /// <summary>Dash recharge, 0 (just used) to 1 (ready). Steps of 1% so the bar does not rebind every frame.</summary>
        [CreateProperty]
        public float DashCharge
        {
            get => _dashCharge;
            private set => Set(ref _dashCharge, value, nameof(DashCharge));
        }

        [CreateProperty]
        public bool DashReady
        {
            get => _dashReady;
            private set => Set(ref _dashReady, value, nameof(DashReady));
        }

        /// <summary>Hit points, whole numbers.</summary>
        [CreateProperty]
        public string HealthText
        {
            get => _healthText;
            private set => Set(ref _healthText, value, nameof(HealthText));
        }

        /// <summary>0 (dead) to 1 (full), steps of 1%.</summary>
        [CreateProperty]
        public float HealthFraction
        {
            get => _healthFraction;
            private set => Set(ref _healthFraction, value, nameof(HealthFraction));
        }

        [CreateProperty]
        public bool HealthLow
        {
            get => _healthLow;
            private set => Set(ref _healthLow, value, nameof(HealthLow));
        }

        /// <summary>False when the gun's aim point is behind the camera.</summary>
        [CreateProperty]
        public bool AimVisible
        {
            get => _aimVisible;
            private set => Set(ref _aimVisible, value, nameof(AimVisible));
        }

        /// <summary>Where the gun points on screen: viewport space, (0,0) bottom left to (1,1) top right.</summary>
        [CreateProperty]
        public Vector2 AimViewport
        {
            get => _aimViewport;
            private set => Set(ref _aimViewport, value, nameof(AimViewport));
        }

        /// <summary>True for a short time after the server confirms a hit.</summary>
        [CreateProperty]
        public bool HitMarkerVisible
        {
            get => _hitMarkerVisible;
            private set => Set(ref _hitMarkerVisible, value, nameof(HitMarkerVisible));
        }

        /// <summary>True for a short time after the server confirms a kill.</summary>
        [CreateProperty]
        public bool KillMarkerVisible
        {
            get => _killMarkerVisible;
            private set => Set(ref _killMarkerVisible, value, nameof(KillMarkerVisible));
        }

        /// <summary>True while the chopper is outside the arena and taking damage.</summary>
        [CreateProperty]
        public bool OutOfBounds
        {
            get => _outOfBounds;
            set => Set(ref _outOfBounds, value, nameof(OutOfBounds));
        }

        /// <summary>True for a short time after a hit from another player.</summary>
        [CreateProperty]
        public bool DamageIndicatorVisible
        {
            get => _damageIndicatorVisible;
            private set => Set(ref _damageIndicatorVisible, value, nameof(DamageIndicatorVisible));
        }

        /// <summary>Where the last hit came from, seen from the camera: 0 = ahead, 90 = right, 180 = behind.</summary>
        [CreateProperty]
        public float DamageDirectionDegrees
        {
            get => _damageDirectionDegrees;
            private set => Set(ref _damageDirectionDegrees, value, nameof(DamageDirectionDegrees));
        }

        /// <summary>Chopper bank angle: positive = rolled left. The horizon line turns by this much (steps of 0.5°).</summary>
        [CreateProperty]
        public float RollDegrees
        {
            get => _rollDegrees;
            private set => Set(ref _rollDegrees, value, nameof(RollDegrees));
        }

        /// <summary>Flight frame offset in pixels (x right, y down). Lags behind turns, so the HUD feels kinetic.</summary>
        [CreateProperty]
        public Vector2 Sway
        {
            get => _sway;
            private set => Set(ref _sway, value, nameof(Sway));
        }

        /// <param name="rollDegrees">Positive = rolled left.</param>
        /// <param name="yawRate">Degrees per second, positive = nose right.</param>
        /// <param name="pitchRate">Degrees per second, positive = nose down.</param>
        public void UpdateAttitude(float rollDegrees, float yawRate, float pitchRate)
        {
            RollDegrees = MathF.Round(rollDegrees * 2f) / 2f;
            // The frame swings against the turn, like a head-up display on a moving head.
            _swayTarget = new Vector2(
                Math.Clamp(-yawRate * SwayPerDegreePerSecond, -MaxSway, MaxSway),
                Math.Clamp(-pitchRate * SwayPerDegreePerSecond, -MaxSway, MaxSway));
        }

        public void Update(float speedMetresPerSecond, float altitudeMetres, float dashCooldownRemaining, float dashCooldown)
        {
            HasChopper = true;
            SpeedText = ((int)MathF.Round(speedMetresPerSecond * MetresPerSecondToKilometresPerHour)).ToString();
            AltitudeText = ((int)MathF.Round(MathF.Max(0f, altitudeMetres))).ToString();

            var charge = dashCooldown > 0f ? 1f - Math.Clamp(dashCooldownRemaining / dashCooldown, 0f, 1f) : 1f;
            DashCharge = MathF.Round(charge * 100f) / 100f;
            DashReady = dashCooldownRemaining <= 0f;
        }

        public void UpdateHealth(float current, float max)
        {
            var fraction = max > 0f ? Math.Clamp(current / max, 0f, 1f) : 0f;
            HealthText = ((int)MathF.Ceiling(MathF.Max(0f, current))).ToString();
            HealthFraction = MathF.Round(fraction * 100f) / 100f;
            HealthLow = fraction < LowHealthFraction;
        }

        /// <param name="viewport">Aim point in viewport space; z below 0 means behind the camera.</param>
        public void UpdateAim(Vector3 viewport)
        {
            AimVisible = viewport.z > 0f;
            if (AimVisible)
                AimViewport = new Vector2(MathF.Round(viewport.x * 1000f) / 1000f, MathF.Round(viewport.y * 1000f) / 1000f);
        }

        /// <summary>Called for each hit the server confirms.</summary>
        public void ShowHit(bool killed)
        {
            _hitMarkerTime = HitMarkerDuration;
            HitMarkerVisible = true;
            if (!killed) return;
            _killMarkerTime = KillMarkerDuration;
            KillMarkerVisible = true;
        }

        /// <param name="directionDegrees">0 = ahead, 90 = right, 180 = behind (camera space).</param>
        public void ShowDamageFrom(float directionDegrees)
        {
            DamageDirectionDegrees = MathF.Round(directionDegrees);
            _damageIndicatorTime = DamageIndicatorDuration;
            DamageIndicatorVisible = true;
        }

        /// <summary>Counts down the hit, kill and damage markers. Call once per frame.</summary>
        public void Tick(float deltaTime)
        {
            _hitMarkerTime = MathF.Max(0f, _hitMarkerTime - deltaTime);
            _killMarkerTime = MathF.Max(0f, _killMarkerTime - deltaTime);
            _damageIndicatorTime = MathF.Max(0f, _damageIndicatorTime - deltaTime);
            HitMarkerVisible = _hitMarkerTime > 0f;
            KillMarkerVisible = _killMarkerTime > 0f;
            DamageIndicatorVisible = _damageIndicatorTime > 0f;

            _swaySmoothed = Vector2.Lerp(_swaySmoothed, _swayTarget, 1f - MathF.Exp(-SwayResponse * deltaTime));
            Sway = new Vector2(MathF.Round(_swaySmoothed.x * 2f) / 2f, MathF.Round(_swaySmoothed.y * 2f) / 2f);
        }

        public void ClearChopper()
        {
            HasChopper = false;
            AimVisible = false;
            OutOfBounds = false;
            _swayTarget = Vector2.zero;
        }

        private void Set<T>(ref T field, T value, string propertyName)
        {
            if (Equals(field, value)) return;
            field = value;
            propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(propertyName));
        }
    }
}
