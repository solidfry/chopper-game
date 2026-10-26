using System;
using Unity.Properties;
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

        private bool _hasChopper;
        private string _speedText = "0";
        private string _altitudeText = "0";
        private float _dashCharge = 1f;
        private bool _dashReady = true;

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

        public void Update(float speedMetresPerSecond, float altitudeMetres, float dashCooldownRemaining, float dashCooldown)
        {
            HasChopper = true;
            SpeedText = ((int)MathF.Round(speedMetresPerSecond * MetresPerSecondToKilometresPerHour)).ToString();
            AltitudeText = ((int)MathF.Round(MathF.Max(0f, altitudeMetres))).ToString();

            var charge = dashCooldown > 0f ? 1f - Math.Clamp(dashCooldownRemaining / dashCooldown, 0f, 1f) : 1f;
            DashCharge = MathF.Round(charge * 100f) / 100f;
            DashReady = dashCooldownRemaining <= 0f;
        }

        public void ClearChopper() => HasChopper = false;

        private void Set<T>(ref T field, T value, string propertyName)
        {
            if (Equals(field, value)) return;
            field = value;
            propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(propertyName));
        }
    }
}
