using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Chopper.Presentation.Camera
{
    /// <summary>
    /// The chopper camera. <see cref="LocalChopperCameraSystem"/> calls <see cref="Follow"/> once per frame with the chopper's
    /// rendered pose, so the camera never lags a frame behind it.
    /// <list type="bullet">
    /// <item>Views come from <see cref="CameraSettings"/> (on the game config): chase (smoothed heading, roll on a delay,
    /// speed pull-back) and mounted (cockpit: rigid to the chopper). C / gamepad Y cycles them; the choice is saved.</item>
    /// <item>Switching blends position, rotation, field of view and near clip over the blend time.</item>
    /// <item>Each view can add noise (handheld / engine wobble); hits add shake (<see cref="AddShake"/>).</item>
    /// <item>A respawn or teleport snaps the camera instead of swinging across the map.</item>
    /// </list>
    /// </summary>
    [RequireComponent(typeof(UnityEngine.Camera))]
    public sealed class ChopperCameraRig : MonoBehaviour
    {
        private const string ViewKey = "Chopper.Camera.View";
        /// <summary>A jump larger than this in one frame (metres) is a respawn or teleport: snap, do not smooth.</summary>
        private const float TeleportDistance = 60f;

        [SerializeField, Tooltip("Optional override. Empty = the game config's camera settings (given by the presentation bridge).")]
        private CameraSettings settings;
        [SerializeField] private bool lockCursor = true;

        private struct Pose
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public float FieldOfView;
            public float NearClip;
        }

        /// <summary>Smoothing state of one chase view.</summary>
        private struct ViewState
        {
            public bool Valid;
            public Vector3 Forward;
            public Vector3 Up;
        }

        private UnityEngine.Camera _camera;
        private CameraSettings _fallback;
        private ViewState[] _states = Array.Empty<ViewState>();
        private int _view;
        private int _previousView = -1;
        private float _blend = 1f;
        private bool _hasLastTarget;
        private Vector3 _lastTargetPosition;
        private float _smoothedSpeed;
        private float _trauma;
        /// <summary>A menu is open (<see cref="SetCursorFree"/>): no view switching.</summary>
        private bool _menuOpen;

        private CameraSettings Settings
        {
            get
            {
                if (settings != null) return settings;
                // No asset yet (old scene): the defaults in code.
                if (_fallback == null) _fallback = ScriptableObject.CreateInstance<CameraSettings>();
                return _fallback;
            }
        }

        public int ViewIndex => _view;
        public string ViewName => ViewAt(_view)?.Name ?? "";
        public bool IsMounted => ViewAt(_view)?.Kind == CameraViewKind.Mounted;

        /// <summary>Called by the presentation context with the game config's settings, unless this rig has its own.</summary>
        public void UseSettings(CameraSettings value)
        {
            if (settings == null && value != null) settings = value;
            _view = Mathf.Clamp(PlayerPrefs.GetInt(ViewKey, 0), 0, Mathf.Max(0, Settings.Views.Count - 1));
            _states = new ViewState[Settings.Views.Count];
        }

        /// <summary>Switches view with a blend (saved).</summary>
        public void SetView(int index)
        {
            var count = Settings.Views.Count;
            if (count == 0) return;
            index = (index % count + count) % count;
            if (index == _view) return;

            _previousView = _view;
            _view = index;
            _blend = Settings.BlendTime > 0f ? 0f : 1f;
            EnsureStates();
            // The new view starts from the chopper's current heading, not from where it was last used.
            _states[_view].Valid = false;
            PlayerPrefs.SetInt(ViewKey, index);
        }

        public void NextView() => SetView(_view + 1);

        /// <summary>Adds camera shake. 0..1; trauma is capped at 1 and the shake grows with its square.</summary>
        public void AddShake(float trauma) => _trauma = Mathf.Clamp01(_trauma + trauma);

        public void Follow(Vector3 targetPosition, Quaternion targetRotation, float deltaTime)
        {
            var settingsAsset = Settings;
            if (settingsAsset.Views.Count == 0 || _camera == null) return;
            EnsureStates();
            UpdateSpeed(targetPosition, deltaTime, settingsAsset);

            var speed01 = Mathf.Clamp01(_smoothedSpeed / settingsAsset.FullEffectSpeed);
            var pose = Evaluate(_view, targetPosition, targetRotation, deltaTime, speed01);

            if (_previousView >= 0 && _previousView < _states.Length && _blend < 1f)
            {
                _blend = Mathf.Min(1f, _blend + deltaTime / Mathf.Max(settingsAsset.BlendTime, 1e-4f));
                var from = Evaluate(_previousView, targetPosition, targetRotation, deltaTime, speed01);
                var t = Mathf.SmoothStep(0f, 1f, _blend);
                pose = new Pose
                {
                    Position = Vector3.Lerp(from.Position, pose.Position, t),
                    Rotation = Quaternion.Slerp(from.Rotation, pose.Rotation, t),
                    FieldOfView = Mathf.Lerp(from.FieldOfView, pose.FieldOfView, t),
                    // The smaller near plane while blending, so neither view cuts the model.
                    NearClip = Mathf.Min(from.NearClip, pose.NearClip),
                };
            }
            else
            {
                _previousView = -1;
            }

            _camera.fieldOfView = pose.FieldOfView;
            _camera.nearClipPlane = pose.NearClip;
            transform.SetPositionAndRotation(pose.Position, pose.Rotation * Shake(deltaTime, settingsAsset));
        }

        private Pose Evaluate(int index, Vector3 targetPosition, Quaternion targetRotation, float deltaTime, float speed01)
        {
            var view = ViewAt(index);
            var pose = view.Kind == CameraViewKind.Mounted
                ? EvaluateMounted(view, targetPosition, targetRotation)
                : EvaluateChase(view, ref _states[index], targetPosition, targetRotation, deltaTime, speed01);
            pose.FieldOfView = Mathf.Lerp(view.FieldOfView, view.MaxFieldOfView, speed01);
            pose.NearClip = view.NearClip;
            pose.Rotation *= Noise(view, index);
            return pose;
        }

        /// <summary>Behind the chopper. Only orientation is smoothed: smoothing position against a moving target jitters.</summary>
        private static Pose EvaluateChase(CameraView view, ref ViewState state, Vector3 targetPosition, Quaternion targetRotation,
            float deltaTime, float speed01)
        {
            var forward = targetRotation * Vector3.forward;
            // Near-vertical noses have no stable heading; keep the last one.
            if (state.Valid && Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.98f)
                forward = state.Forward;
            var rolledUp = Vector3.Slerp(Vector3.up, targetRotation * Vector3.up, view.RollFollow);

            if (!state.Valid)
            {
                state.Valid = true;
                state.Forward = forward;
                state.Up = rolledUp;
            }
            else if (deltaTime > 0f)
            {
                state.Forward = Vector3.Slerp(state.Forward, forward, 1f - Mathf.Exp(-view.HeadingSharpness * deltaTime)).normalized;
                // Roll follows on its own, slower curve: the horizon tilts with the chopper a moment later.
                state.Up = Vector3.Slerp(state.Up, rolledUp, 1f - Mathf.Exp(-view.RollSharpness * deltaTime)).normalized;
            }

            var frame = Quaternion.LookRotation(state.Forward, state.Up);
            var position = targetPosition + frame * (view.Offset * (1f + view.PullBack * speed01));
            var lookPoint = targetPosition + state.Forward * view.LookAhead;
            return new Pose { Position = position, Rotation = Quaternion.LookRotation(lookPoint - position, state.Up) };
        }

        /// <summary>Rigid to the chopper: the horizon tilts and turns with it, as from the pilot's seat.</summary>
        private static Pose EvaluateMounted(CameraView view, Vector3 targetPosition, Quaternion targetRotation) => new()
        {
            Position = targetPosition + targetRotation * view.Offset,
            Rotation = targetRotation * Quaternion.LookRotation(view.LookPoint - view.Offset, Vector3.up),
        };

        private static Quaternion Noise(CameraView view, int index)
        {
            if (view.NoiseAmplitude <= 0f) return Quaternion.identity;
            var t = Time.time * view.NoiseFrequency;
            var seed = index * 7.31f;
            return Quaternion.Euler(
                view.NoiseAmplitude * (Mathf.PerlinNoise(t, 0.4f + seed) * 2f - 1f),
                view.NoiseAmplitude * (Mathf.PerlinNoise(0.9f + seed, t) * 2f - 1f),
                view.NoiseAmplitude * 0.5f * (Mathf.PerlinNoise(t + seed, 2.3f) * 2f - 1f));
        }

        private void UpdateSpeed(Vector3 targetPosition, float deltaTime, CameraSettings settingsAsset)
        {
            if (_hasLastTarget && Vector3.Distance(targetPosition, _lastTargetPosition) > TeleportDistance)
            {
                // Respawn or teleport: start every view fresh.
                for (var i = 0; i < _states.Length; i++) _states[i].Valid = false;
                _smoothedSpeed = 0f;
                _blend = 1f;
                _hasLastTarget = false;
            }

            if (_hasLastTarget && deltaTime > 0f)
            {
                var speed = (targetPosition - _lastTargetPosition).magnitude / deltaTime;
                _smoothedSpeed = Mathf.Lerp(_smoothedSpeed, speed, 1f - Mathf.Exp(-settingsAsset.SpeedSmoothing * deltaTime));
            }
            _lastTargetPosition = targetPosition;
            _hasLastTarget = true;
        }

        private Quaternion Shake(float deltaTime, CameraSettings settingsAsset)
        {
            if (_trauma <= 0f) return Quaternion.identity;
            _trauma = Mathf.Max(0f, _trauma - settingsAsset.ShakeRecovery * deltaTime);

            var amount = _trauma * _trauma * settingsAsset.MaxShakeDegrees;
            var t = Time.time * settingsAsset.ShakeFrequency;
            return Quaternion.Euler(
                amount * (Mathf.PerlinNoise(t, 0.1f) * 2f - 1f),
                amount * (Mathf.PerlinNoise(0.3f, t) * 2f - 1f),
                amount * 0.5f * (Mathf.PerlinNoise(t, 0.7f) * 2f - 1f));
        }

        private CameraView ViewAt(int index)
        {
            var views = Settings.Views;
            return index >= 0 && index < views.Count ? views[index] : null;
        }

        /// <summary>Keeps one state per view, also after the views list was edited in play mode.</summary>
        private void EnsureStates()
        {
            if (_states.Length == Settings.Views.Count) return;
            Array.Resize(ref _states, Settings.Views.Count);
            _view = Mathf.Clamp(_view, 0, Mathf.Max(0, _states.Length - 1));
            if (_previousView >= _states.Length) _previousView = -1;
        }

        private void Update()
        {
            var keyboard = Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame;
            var gamepad = Gamepad.current != null && Gamepad.current.buttonNorth.wasPressedThisFrame;
            if ((keyboard || gamepad) && !_menuOpen) NextView();
        }

        private void Awake()
        {
            _camera = GetComponent<UnityEngine.Camera>();
            UseSettings(settings);
        }

        private void OnDestroy()
        {
            if (_fallback != null) Destroy(_fallback);
        }

        private void OnEnable()
        {
            if (!lockCursor) return;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        /// <summary>Free: cursor visible and unlocked (a menu is open). Not free: back to this rig's setting.</summary>
        public void SetCursorFree(bool free)
        {
            _menuOpen = free;
            var locked = !free && lockCursor && isActiveAndEnabled;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
