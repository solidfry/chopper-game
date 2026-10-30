using System;
using System.Collections.Generic;
using Chopper.Simulation.Config;
using UnityEngine;

namespace Chopper.Presentation.Camera
{
    public enum CameraViewKind
    {
        /// <summary>Behind the chopper: heading and roll follow with smoothing, distance and FOV grow with speed.</summary>
        Chase = 0,
        /// <summary>Fixed to the chopper (cockpit, gunner seat): moves and rolls rigidly with it.</summary>
        Mounted = 1,
    }

    /// <summary>One camera view. The camera cycles through <see cref="CameraSettings.Views"/> with C / gamepad Y.</summary>
    [Serializable]
    public sealed class CameraView
    {
        public string Name = "Chase";
        public CameraViewKind Kind = CameraViewKind.Chase;

        [Header("Placement (chopper space)")]
        [Tooltip("Chase: camera position behind the chopper. Mounted: the eye position.")]
        public Vector3 Offset = new(0f, 4.5f, -15f);
        [Tooltip("Chase: metres ahead of the chopper the camera looks at. Mounted: not used.")]
        public float LookAhead = 25f;
        [Tooltip("Mounted: where the eye looks (the old ReticalPosition). Chase: not used.")]
        public Vector3 LookPoint = new(0f, -1.5f, 79f);
        [Tooltip("Mounted: always use Offset. Off = sit at the chassis's cockpit eye when it has a cockpit (gunner seats turn this on).")]
        public bool FixedEye;
        [Tooltip("Mounted: still draw your own chopper's hull and modules. Off (cockpit): only the cockpit model shows; the " +
                 "hull's outside details would float in the view, and the pilot sees out through a camera feed.")]
        public bool ShowOwnChopper;

        [Header("Lens")]
        public float FieldOfView = 65f;
        [Tooltip("Field of view at full speed (sense of speed).")]
        public float MaxFieldOfView = 85f;
        [Tooltip("Near clip plane. Small inside a cockpit, so the canopy and dashboard are not cut away.")]
        public float NearClip = 0.3f;

        [Header("Follow (chase)")]
        [Tooltip("How fast the camera swings round to a new heading (1/s).")]
        public float HeadingSharpness = 8f;
        [Range(0f, 1f), Tooltip("How much of the chopper's roll the camera follows. 1 = all of it.")]
        public float RollFollow = 0.85f;
        [Tooltip("How fast the camera catches up with the roll (1/s). Lower = more delay.")]
        public float RollSharpness = 4f;
        [Tooltip("Extra distance at full speed, as a fraction of the offset.")]
        public float PullBack = 0.3f;

        [Header("Noise (handheld / engine wobble)")]
        [Tooltip("Degrees.")] public float NoiseAmplitude;
        [Tooltip("Cycles per second.")] public float NoiseFrequency = 1f;

        [Header("Feel (this view's share of the shared effects; 1 = all)")]
        [Range(0f, 1f), Tooltip("Inertia lag: the chopper pulls away when it speeds up. Keep near 0 in a cockpit (the eye is fixed to the seat).")]
        public float Inertia = 1f;
        [Tooltip("Extra field of view at full boost, degrees.")]
        public float BoostFieldOfView = 14f;
        [Range(0f, 1f), Tooltip("Engine rumble while boosting.")]
        public float Rumble = 1f;
        [Range(0f, 1f), Tooltip("Shake from hits and the boost kick.")]
        public float Shake = 1f;
        [Range(0f, 1f), Tooltip("Speed and boost post effects (colour split, vignette, flash) and speed lines.")]
        public float Effects = 1f;

        /// <summary>The feel values of two views mixed (view blends).</summary>
        public static ViewFeel Mix(CameraView from, CameraView to, float t) => new()
        {
            Inertia = Mathf.Lerp(from?.Inertia ?? to.Inertia, to.Inertia, t),
            Rumble = Mathf.Lerp(from?.Rumble ?? to.Rumble, to.Rumble, t),
            Shake = Mathf.Lerp(from?.Shake ?? to.Shake, to.Shake, t),
            Effects = Mathf.Lerp(from?.Effects ?? to.Effects, to.Effects, t),
        };
    }

    /// <summary>How much of the shared camera effects the current view (or blend) gets.</summary>
    public struct ViewFeel
    {
        public float Inertia;
        public float Rumble;
        public float Shake;
        public float Effects;
    }

    /// <summary>
    /// The chopper camera: its views, blends between them, speed effects and hit shake. Hangs off
    /// <see cref="Config.GameConfig"/>; read by <see cref="ChopperCameraRig"/>. Made by Chopper → Setup.
    /// Defaults are the tuned chase view and the old game's cockpit camera.
    /// </summary>
    [CreateAssetMenu(menuName = "Chopper/Camera/Camera Settings", fileName = "CameraSettings")]
    public sealed class CameraSettings : ScriptableObject, IValidatedAsset
    {
        [SerializeField, Tooltip("C / gamepad Y cycles through these, in order. The first is the default.")]
        private List<CameraView> views = new()
        {
            new CameraView
            {
                Name = "Chase", Kind = CameraViewKind.Chase, Offset = new Vector3(0f, 4.5f, -15f), LookAhead = 25f,
                FieldOfView = 65f, MaxFieldOfView = 85f, NearClip = 0.3f, HeadingSharpness = 8f, RollFollow = 0.85f,
                RollSharpness = 4f, PullBack = 0.3f, NoiseAmplitude = 0.15f, NoiseFrequency = 0.6f, Inertia = 1f,
                BoostFieldOfView = 14f, Rumble = 1f, Shake = 1f, Effects = 1f,
            },
            new CameraView
            {
                // The old game's Cinemachine cockpit camera.
                Name = "Cockpit", Kind = CameraViewKind.Mounted, Offset = new Vector3(0f, -0.51f, 2.47f),
                LookPoint = new Vector3(0f, -1.5f, 79f), FieldOfView = 70f, MaxFieldOfView = 73f, NearClip = 0.05f,
                // The eye sits in the seat: almost no lag, a small boost punch, light shake, few post effects (they
                // would cross the dashboard too).
                NoiseAmplitude = 0.06f, NoiseFrequency = 3f, Inertia = 0f,
                BoostFieldOfView = 4f, Rumble = 0.35f, Shake = 0.45f, Effects = 0.3f,
            },
        };

        [Header("Blend between views")]
        [SerializeField, Tooltip("Seconds. 0 = cut.")] private float blendTime = 0.35f;

        [Header("Sense of speed")]
        [SerializeField, Tooltip("Speed (m/s) at which the speed effects are at full strength.")]
        private float fullEffectSpeed = 80f;
        [SerializeField] private float speedSmoothing = 3f;

        [Header("Shake (hits taken)")]
        [SerializeField, Tooltip("Largest shake angle in degrees, at full trauma.")]
        private float maxShakeDegrees = 4f;
        [SerializeField, Tooltip("Trauma lost per second.")] private float shakeRecovery = 1.5f;
        [SerializeField] private float shakeFrequency = 18f;

        [Header("Weight (inertia lag)")]
        [SerializeField, Tooltip("Metres the camera falls behind per m/s² of acceleration: the chopper pulls away when it speeds up, "
            + "and closes in when it brakes.")]
        private float accelerationLag = 0.06f;
        [SerializeField, Tooltip("Largest lag, metres.")] private float maxLag = 3f;
        [SerializeField, Tooltip("How fast the lag follows (1/s). Lower = more weight.")] private float lagSharpness = 4f;

        [Header("Boost (each view sets its own boost field of view, rumble and shake share)")]
        [SerializeField, Tooltip("Chase views: extra distance at full boost, as a fraction of the offset.")] private float boostPullBack = 0.3f;
        [SerializeField, Tooltip("Spring that drives the boost look. Damping below 1 overshoots: the punch.")]
        private float boostSpringFrequency = 5f;
        [SerializeField, Range(0.1f, 1f)] private float boostSpringDamping = 0.45f;
        [SerializeField, Tooltip("Trauma added when a boost starts (0..1).")] private float boostKickShake = 0.4f;
        [SerializeField, Tooltip("Engine rumble while boosting: degrees of shake.")] private float boostRumbleDegrees = 0.45f;
        [SerializeField, Tooltip("Engine rumble while boosting: metres of camera jolt (the chopper seems to shudder).")]
        private float boostRumbleOffset = 0.07f;
        [SerializeField] private float boostRumbleFrequency = 24f;
        [SerializeField, Tooltip("HUD glitch when a boost starts (0..1). 0 = none.")] private float boostHudGlitch = 0.3f;

        [Header("Post effects (a runtime volume the camera owns)")]
        [SerializeField] private bool postEffects = true;
        [SerializeField] private float bloomIntensity = 0.7f;
        [SerializeField] private float bloomThreshold = 0.95f;
        [SerializeField, Tooltip("Brightest value that goes into bloom. Stops one very bright pixel from flashing the whole screen.")]
        private float bloomClamp = 20f;
        [SerializeField, Range(0f, 1f), Tooltip("Chromatic aberration at full speed / extra at full boost.")]
        private float speedChromatic = 0.12f;
        [SerializeField, Range(0f, 1f)] private float boostChromatic = 0.55f;
        [SerializeField, Range(0f, 1f), Tooltip("Vignette while boosting.")] private float boostVignette = 0.32f;
        [SerializeField, Tooltip("Exposure flash when a boost starts (EV).")] private float boostFlash = 0.6f;

        [Header("Speed lines (colours: Dream Cyan / Cruise Pink from the style guide, HDR for bloom)")]
        [SerializeField, Tooltip("Hidden/Chopper/SpeedLine. Empty = no speed lines.")] private Shader speedLineShader;
        [SerializeField, Tooltip("Speed (m/s) where the lines start.")] private float speedLineStartSpeed = 35f;
        [SerializeField, Tooltip("Lines per second at full speed / extra at full boost.")] private float speedLineRate = 40f;
        [SerializeField] private float speedLineBoostRate = 150f;
        [SerializeField, ColorUsage(true, true)] private Color speedLineColor = new(0f, 1.5f, 1.6f, 0.35f);
        [SerializeField, ColorUsage(true, true)] private Color speedLineBoostColor = new(1.8f, 0.47f, 0.97f, 0.45f);

        public IReadOnlyList<CameraView> Views => views;
        public float BlendTime => blendTime;
        public float FullEffectSpeed => fullEffectSpeed;
        public float SpeedSmoothing => speedSmoothing;
        public float MaxShakeDegrees => maxShakeDegrees;
        public float ShakeRecovery => shakeRecovery;
        public float ShakeFrequency => shakeFrequency;
        public float AccelerationLag => accelerationLag;
        public float MaxLag => maxLag;
        public float LagSharpness => lagSharpness;
        public float BoostPullBack => boostPullBack;
        public float BoostSpringFrequency => boostSpringFrequency;
        public float BoostSpringDamping => boostSpringDamping;
        public float BoostKickShake => boostKickShake;
        public float BoostRumbleDegrees => boostRumbleDegrees;
        public float BoostRumbleOffset => boostRumbleOffset;
        public float BoostRumbleFrequency => boostRumbleFrequency;
        public float BoostHudGlitch => boostHudGlitch;
        public bool PostEffects => postEffects;
        public float BloomIntensity => bloomIntensity;
        public float BloomThreshold => bloomThreshold;
        public float BloomClamp => bloomClamp;
        public float SpeedChromatic => speedChromatic;
        public float BoostChromatic => boostChromatic;
        public float BoostVignette => boostVignette;
        public float BoostFlash => boostFlash;
        public Shader SpeedLineShader => speedLineShader;
        public float SpeedLineStartSpeed => speedLineStartSpeed;
        public float SpeedLineRate => speedLineRate;
        public float SpeedLineBoostRate => speedLineBoostRate;
        public Color SpeedLineColor => speedLineColor;
        public Color SpeedLineBoostColor => speedLineBoostColor;

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetSpeedLineShader(Shader shader) => speedLineShader = shader;

        public void CollectProblems(List<string> problems)
        {
            if (views == null || views.Count == 0) problems.Add($"{name}: no camera views.");
            else
                foreach (var view in views)
                {
                    if (view == null) { problems.Add($"{name}: an empty camera view."); continue; }
                    if (view.NearClip <= 0f) problems.Add($"{name}: view {view.Name} near clip must be above 0.");
                    if (view.FieldOfView <= 0f || view.FieldOfView >= 180f) problems.Add($"{name}: view {view.Name} field of view must be 1..179.");
                }
            if (fullEffectSpeed <= 0f) problems.Add($"{name}: full effect speed must be above 0.");
            if (speedLineShader == null) problems.Add($"{name}: no speed line shader (no speed lines). Run Chopper → Setup.");
        }
    }
}
