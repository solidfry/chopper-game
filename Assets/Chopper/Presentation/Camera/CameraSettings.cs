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
                RollSharpness = 4f, PullBack = 0.3f, NoiseAmplitude = 0.15f, NoiseFrequency = 0.6f,
            },
            new CameraView
            {
                // The old game's Cinemachine cockpit camera.
                Name = "Cockpit", Kind = CameraViewKind.Mounted, Offset = new Vector3(0f, -0.51f, 2.47f),
                LookPoint = new Vector3(0f, -1.5f, 79f), FieldOfView = 70f, MaxFieldOfView = 76f, NearClip = 0.05f,
                NoiseAmplitude = 0.25f, NoiseFrequency = 6f,
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

        public IReadOnlyList<CameraView> Views => views;
        public float BlendTime => blendTime;
        public float FullEffectSpeed => fullEffectSpeed;
        public float SpeedSmoothing => speedSmoothing;
        public float MaxShakeDegrees => maxShakeDegrees;
        public float ShakeRecovery => shakeRecovery;
        public float ShakeFrequency => shakeFrequency;

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
        }
    }
}
