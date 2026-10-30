using System;
using System.Collections.Generic;
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
        /// <summary>Missiles further than this give the slowest warning.</summary>
        public const float MissileWarningRange = 800f;
        /// <summary>How fast the frame follows the target sway (1/s). Lower = more lag, more weight.</summary>
        private const float SwayResponse = 6f;

        private bool _hasChopper;
        private string _speedText = "0";
        private int _speed;
        private string _altitudeText = "0";
        private int _altitude;
        private float _boostEnergy = 1f;
        private bool _boostReady = true;
        private bool _boosting;
        private string _healthText = "0";
        private int _health;
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
        private float _pitchDegrees;
        private float _headingDegrees;
        private Vector2 _sway;
        private Vector2 _swayTarget;
        private Vector2 _swaySmoothed;
        private bool _lockVisible;
        private Vector2 _lockViewport = new(0.5f, 0.5f);
        private float _lockProgress;
        private bool _locked;
        private string _pylonText = "";
        private bool _pylonNeedsLock;
        private bool _flaresReady;
        private bool _threatLocking;
        private bool _threatLocked;
        private bool _missileInbound;
        private float _missileDirectionDegrees;
        private float _missileUrgency;
        private bool _warningBlink;
        private float _blinkTime;

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

        /// <summary>Boost tank, 0 (empty) to 1 (full). Steps of 1% so the bar does not rebind every frame.</summary>
        [CreateProperty]
        public float BoostEnergy
        {
            get => _boostEnergy;
            private set => Set(ref _boostEnergy, value, nameof(BoostEnergy));
        }

        /// <summary>The tank holds enough to start a boost.</summary>
        [CreateProperty]
        public bool BoostReady
        {
            get => _boostReady;
            private set => Set(ref _boostReady, value, nameof(BoostReady));
        }

        [CreateProperty]
        public bool Boosting
        {
            get => _boosting;
            private set => Set(ref _boosting, value, nameof(Boosting));
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

        /// <summary>True while the selected seeker is locking or has locked a target on screen.</summary>
        [CreateProperty] public bool LockVisible { get => _lockVisible; private set => Set(ref _lockVisible, value, nameof(LockVisible)); }

        /// <summary>Where the lock target is on screen, viewport space (like <see cref="AimViewport"/>).</summary>
        [CreateProperty] public Vector2 LockViewport { get => _lockViewport; private set => Set(ref _lockViewport, value, nameof(LockViewport)); }

        /// <summary>0 = lock just started, 1 = locked. Steps of 5%.</summary>
        [CreateProperty] public float LockProgress { get => _lockProgress; private set => Set(ref _lockProgress, value, nameof(LockProgress)); }

        [CreateProperty] public bool Locked { get => _locked; private set => Set(ref _locked, value, nameof(Locked)); }

        /// <summary>Selected pylon weapon, e.g. "ROCKET POD"; empty with no pylon weapons.</summary>
        [CreateProperty] public string PylonText { get => _pylonText; private set => Set(ref _pylonText, value, nameof(PylonText)); }

        /// <summary>The selected pylon weapon cannot fire without a lock.</summary>
        [CreateProperty] public bool PylonNeedsLock { get => _pylonNeedsLock; private set => Set(ref _pylonNeedsLock, value, nameof(PylonNeedsLock)); }

        [CreateProperty] public bool FlaresReady { get => _flaresReady; private set => Set(ref _flaresReady, value, nameof(FlaresReady)); }

        /// <summary>An enemy seeker is building a lock on you.</summary>
        [CreateProperty] public bool ThreatLocking { get => _threatLocking; private set => Set(ref _threatLocking, value, nameof(ThreatLocking)); }

        /// <summary>An enemy has a lock on you.</summary>
        [CreateProperty] public bool ThreatLocked { get => _threatLocked; private set => Set(ref _threatLocked, value, nameof(ThreatLocked)); }

        [CreateProperty] public bool MissileInbound { get => _missileInbound; private set => Set(ref _missileInbound, value, nameof(MissileInbound)); }

        /// <summary>Where the closest missile comes from, seen from the camera (0 = ahead, 90 = right).</summary>
        [CreateProperty] public float MissileDirectionDegrees { get => _missileDirectionDegrees; private set => Set(ref _missileDirectionDegrees, value, nameof(MissileDirectionDegrees)); }

        /// <summary>0 = far (or none), 1 = about to hit. Steps of 5%. Drives the warning blink and tone speed.</summary>
        [CreateProperty] public float MissileUrgency { get => _missileUrgency; private set => Set(ref _missileUrgency, value, nameof(MissileUrgency)); }

        /// <summary>Blinks the threat warnings; faster as a missile closes in.</summary>
        [CreateProperty] public bool WarningBlink { get => _warningBlink; private set => Set(ref _warningBlink, value, nameof(WarningBlink)); }

        /// <param name="lockViewport">Lock target in viewport space; z below 0 = behind the camera (lock hidden).</param>
        public void UpdateTargeting(bool hasLockTarget, Vector3 lockViewport, float progress, bool locked, string pylonName, bool needsLock,
            bool flaresReady)
        {
            LockVisible = hasLockTarget && lockViewport.z > 0f;
            if (LockVisible)
                LockViewport = new Vector2(MathF.Round(lockViewport.x * 1000f) / 1000f, MathF.Round(lockViewport.y * 1000f) / 1000f);
            LockProgress = hasLockTarget ? MathF.Round(Math.Clamp(progress, 0f, 1f) * 20f) / 20f : 0f;
            Locked = hasLockTarget && locked;
            PylonText = pylonName ?? "";
            PylonNeedsLock = needsLock;
            FlaresReady = flaresReady;
        }

        /// <param name="missileDistance">Distance to the closest missile chasing you; ignored when none.</param>
        public void UpdateThreats(bool locking, bool lockedOn, bool missileInbound, float missileDirectionDegrees, float missileDistance)
        {
            ThreatLocking = locking;
            ThreatLocked = lockedOn;
            MissileInbound = missileInbound;
            if (missileInbound) MissileDirectionDegrees = MathF.Round(missileDirectionDegrees);
            var urgency = missileInbound ? 1f - Math.Clamp(missileDistance / MissileWarningRange, 0f, 1f) : 0f;
            MissileUrgency = MathF.Round(urgency * 20f) / 20f;
        }

        /// <summary>Chopper bank angle: positive = rolled left. The horizon line turns by this much (steps of 0.5°).</summary>
        [CreateProperty]
        public float RollDegrees
        {
            get => _rollDegrees;
            private set => Set(ref _rollDegrees, value, nameof(RollDegrees));
        }

        /// <summary>Nose above the horizon, degrees (negative = nose down; steps of 0.5°). Cockpit flight display.</summary>
        [CreateProperty]
        public float PitchDegrees
        {
            get => _pitchDegrees;
            private set => Set(ref _pitchDegrees, value, nameof(PitchDegrees));
        }

        /// <summary>Compass heading, whole degrees 0..359 (0 = world +Z). Cockpit flight display.</summary>
        [CreateProperty]
        public float HeadingDegrees
        {
            get => _headingDegrees;
            private set => Set(ref _headingDegrees, value, nameof(HeadingDegrees));
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
        /// <param name="pitchDegrees">Nose above the horizon (negative = down).</param>
        /// <param name="headingDegrees">Compass heading, any range (wrapped to 0..359).</param>
        public void UpdateAttitude(float rollDegrees, float yawRate, float pitchRate, float pitchDegrees = 0f, float headingDegrees = 0f)
        {
            RollDegrees = MathF.Round(rollDegrees * 2f) / 2f;
            PitchDegrees = MathF.Round(pitchDegrees * 2f) / 2f;
            HeadingDegrees = (MathF.Round(headingDegrees) % 360f + 360f) % 360f;
            // The frame swings against the turn, like a head-up display on a moving head.
            _swayTarget = new Vector2(
                Math.Clamp(-yawRate * SwayPerDegreePerSecond, -MaxSway, MaxSway),
                Math.Clamp(-pitchRate * SwayPerDegreePerSecond, -MaxSway, MaxSway));
        }

        /// <param name="boostEnergy">Boost tank, 0..1.</param>
        /// <param name="boostStartEnergy">Tank needed to start a boost.</param>
        public void Update(float speedMetresPerSecond, float altitudeMetres, float boostEnergy, bool boosting, float boostStartEnergy)
        {
            HasChopper = true;
            if (Changed(ref _speed, (int)MathF.Round(speedMetresPerSecond * MetresPerSecondToKilometresPerHour)))
                SpeedText = _speed.ToString();
            if (Changed(ref _altitude, (int)MathF.Round(MathF.Max(0f, altitudeMetres))))
                AltitudeText = _altitude.ToString();

            var energy = Math.Clamp(boostEnergy, 0f, 1f);
            BoostEnergy = MathF.Round(energy * 100f) / 100f;
            Boosting = boosting;
            BoostReady = boosting || energy >= boostStartEnergy;
        }

        public void UpdateHealth(float current, float max)
        {
            var fraction = max > 0f ? Math.Clamp(current / max, 0f, 1f) : 0f;
            if (Changed(ref _health, (int)MathF.Ceiling(MathF.Max(0f, current))))
                HealthText = _health.ToString();
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

            TickWarningBlink(deltaTime);

            _swaySmoothed = Vector2.Lerp(_swaySmoothed, _swayTarget, 1f - MathF.Exp(-SwayResponse * deltaTime));
            Sway = new Vector2(MathF.Round(_swaySmoothed.x * 2f) / 2f, MathF.Round(_swaySmoothed.y * 2f) / 2f);
        }

        /// <summary>Missile: blink faster as it closes in (0.35 s to 0.08 s). Lock warning only: steady slow blink.</summary>
        private void TickWarningBlink(float deltaTime)
        {
            if (!MissileInbound && !ThreatLocked && !ThreatLocking)
            {
                WarningBlink = false;
                _blinkTime = 0f;
                return;
            }

            var interval = MissileInbound ? 0.35f + (0.08f - 0.35f) * MissileUrgency : 0.3f;
            _blinkTime += deltaTime;
            if (_blinkTime < interval) return;
            _blinkTime = 0f;
            WarningBlink = !WarningBlink;
        }

        public void ClearChopper()
        {
            HasChopper = false;
            AimVisible = false;
            OutOfBounds = false;
            _swayTarget = Vector2.zero;
            UpdateTargeting(false, Vector3.back, 0f, false, "", false, false);
            UpdateThreats(false, false, false, 0f, 0f);
        }

        /// <summary>Numbers become text only when they change: no string per frame.</summary>
        private static bool Changed(ref int shown, int value)
        {
            if (shown == value) return false;
            shown = value;
            return true;
        }

        private void Set<T>(ref T field, T value, string propertyName)
        {
            // Not object.Equals: that boxes value types (garbage every frame).
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(propertyName));
        }
    }
}
