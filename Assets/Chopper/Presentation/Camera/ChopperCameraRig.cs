using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Chopper.Presentation.Camera
{
    /// <summary>What the camera follows this frame: the local chopper's rendered pose and its motion.</summary>
    public struct CameraTarget
    {
        public Vector3 Position;
        public Quaternion Rotation;
        /// <summary>World velocity, m/s.</summary>
        public Vector3 Velocity;
        public bool Boosting;
    }

    /// <summary>
    /// The chopper camera. <see cref="LocalChopperCameraSystem"/> calls <see cref="Follow"/> once per frame with the chopper's
    /// rendered pose, so the camera never lags a frame behind it.
    /// <list type="bullet">
    /// <item>Views come from <see cref="CameraSettings"/> (on the game config): chase (smoothed heading, roll on a delay,
    /// speed pull-back) and mounted (cockpit: rigid to the chopper). C / gamepad Y cycles them; the choice is saved.</item>
    /// <item>Switching blends position, rotation, field of view and near clip over the blend time.</item>
    /// <item>Each view can add noise (handheld / engine wobble); hits add shake (<see cref="AddShake"/>).</item>
    /// <item>Weight: the camera lags with acceleration (the chopper pulls away when it speeds up, closes in when it brakes,
    /// swings out in turns).</item>
    /// <item>Boost: a spring drives wider FOV and pull-back with a punch, a shake kick and an engine rumble; post effects
    /// and speed lines come from <see cref="SpeedEffects"/>.</item>
    /// <item>A respawn or teleport snaps the camera instead of swinging across the map.</item>
    /// </list>
    /// </summary>
    [RequireComponent(typeof(UnityEngine.Camera))]
    public sealed class ChopperCameraRig : MonoBehaviour
    {
        private const string ViewKey = "Chopper.Camera.View";
        /// <summary>A jump larger than this in one frame (metres) is a respawn or teleport: snap, do not smooth.</summary>
        private const float TeleportDistance = 60f;
        /// <summary>Seconds the boost must be off before a new boost counts as a new kick.</summary>
        private const float MinBoostGap = 0.3f;

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
        private SpeedEffects _effects;
        private Vector3 _lastVelocity;
        private bool _hasLastVelocity;
        private Vector3 _acceleration;
        private Vector3 _lag;
        private float _boost;
        private float _boostVelocity;
        private bool _wasBoosting;
        private float _boostEndTime = float.NegativeInfinity;
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

        /// <summary>The settings in use (own override, the game config's, or code defaults).</summary>
        public CameraSettings ActiveSettings => Settings;
        /// <summary>A boost started this frame (set by <see cref="Follow"/>).</summary>
        public bool BoostKicked { get; private set; }
        public int ViewIndex => _view;
        public string ViewName => ViewAt(_view)?.Name ?? "";
        public bool IsMounted => ViewAt(_view)?.Kind == CameraViewKind.Mounted;

        /// <summary>Called by the presentation context with the game config's settings, unless this rig has its own.</summary>
        public void UseSettings(CameraSettings value)
        {
            if (settings == null && value != null) settings = value;
            _view = Mathf.Clamp(PlayerPrefs.GetInt(ViewKey, 0), 0, Mathf.Max(0, Settings.Views.Count - 1));
            _states = new ViewState[Settings.Views.Count];
            if (_camera == null) return;
            // The effects read their shader from the settings: make them again for new settings.
            _effects?.Dispose();
            _effects = new SpeedEffects(transform, _camera, Settings);
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

        public void Follow(in CameraTarget target, float deltaTime)
        {
            var settingsAsset = Settings;
            if (settingsAsset.Views.Count == 0 || _camera == null) return;
            EnsureStates();
            var targetPosition = target.Position;
            var targetRotation = target.Rotation;
            UpdateSpeed(target, deltaTime, settingsAsset);
            UpdateLag(target.Velocity, deltaTime, settingsAsset);
            UpdateBoost(target.Boosting, deltaTime, settingsAsset);

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

            var blendT = _previousView >= 0 ? Mathf.SmoothStep(0f, 1f, _blend) : 1f;
            var inertia = Mathf.Lerp(ViewAt(_previousView)?.Inertia ?? 1f, ViewAt(_view).Inertia, blendT);
            var rumble = Rumble(settingsAsset, inertia, out var jolt);
            _camera.fieldOfView = pose.FieldOfView;
            _camera.nearClipPlane = pose.NearClip;
            transform.SetPositionAndRotation(pose.Position + pose.Rotation * jolt, pose.Rotation * rumble * Shake(deltaTime, settingsAsset));
            _effects?.Apply(settingsAsset, _smoothedSpeed, speed01, _boost, BoostKicked, deltaTime);
        }

        /// <summary>
        /// Inertia lag: the camera falls behind the chopper's acceleration and catches up on a slow curve. Acceleration
        /// comes from the physics velocity (steps once per tick), so it is smoothed before use.
        /// </summary>
        private void UpdateLag(Vector3 velocity, float deltaTime, CameraSettings settingsAsset)
        {
            if (deltaTime <= 0f) return;
            if (!_hasLastVelocity)
            {
                _lastVelocity = velocity;
                _hasLastVelocity = true;
            }
            var acceleration = (velocity - _lastVelocity) / deltaTime;
            _lastVelocity = velocity;
            _acceleration = Vector3.Lerp(_acceleration, acceleration, 1f - Mathf.Exp(-10f * deltaTime));

            var target = Vector3.ClampMagnitude(-_acceleration * settingsAsset.AccelerationLag, settingsAsset.MaxLag);
            _lag = Vector3.Lerp(_lag, target, 1f - Mathf.Exp(-settingsAsset.LagSharpness * deltaTime));
        }

        /// <summary>Boost look on a spring: it overshoots on the kick (the punch) and settles while the boost lasts.</summary>
        private void UpdateBoost(bool boosting, float deltaTime, CameraSettings settingsAsset)
        {
            // A kick only after the boost was off for a moment: a prediction correction can flip Boosting off and on
            // for one frame, which must not flash, shake and glitch the screen again.
            BoostKicked = boosting && !_wasBoosting && Time.time - _boostEndTime > MinBoostGap;
            if (!boosting && _wasBoosting) _boostEndTime = Time.time;
            _wasBoosting = boosting;
            if (BoostKicked) AddShake(settingsAsset.BoostKickShake);

            var dt = Mathf.Min(deltaTime, 0.05f);
            var omega = 2f * Mathf.PI * Mathf.Max(settingsAsset.BoostSpringFrequency, 0.1f) / 2f;
            var target = boosting ? 1f : 0f;
            _boostVelocity += (omega * omega * (target - _boost) - 2f * settingsAsset.BoostSpringDamping * omega * _boostVelocity) * dt;
            _boost += _boostVelocity * dt;
        }

        /// <summary>Engine rumble while boosting: fast small shake and a jolt of the camera (the chopper seems to shudder).</summary>
        private Quaternion Rumble(CameraSettings settingsAsset, float inertia, out Vector3 jolt)
        {
            var amount = Mathf.Clamp01(_boost) * Mathf.Lerp(0.35f, 1f, inertia);
            jolt = Vector3.zero;
            if (amount <= 0.001f) return Quaternion.identity;

            var t = Time.time * settingsAsset.BoostRumbleFrequency;
            var degrees = settingsAsset.BoostRumbleDegrees * amount;
            var offset = settingsAsset.BoostRumbleOffset * amount;
            jolt = new Vector3(
                offset * (Mathf.PerlinNoise(t, 5.1f) * 2f - 1f),
                offset * (Mathf.PerlinNoise(6.3f, t) * 2f - 1f),
                0f);
            return Quaternion.Euler(
                degrees * (Mathf.PerlinNoise(t, 3.7f) * 2f - 1f),
                degrees * (Mathf.PerlinNoise(4.2f, t) * 2f - 1f),
                degrees * 0.6f * (Mathf.PerlinNoise(t + 9.1f, 1.3f) * 2f - 1f));
        }

        private Pose Evaluate(int index, Vector3 targetPosition, Quaternion targetRotation, float deltaTime, float speed01)
        {
            var view = ViewAt(index);
            var pose = view.Kind == CameraViewKind.Mounted
                ? EvaluateMounted(view, targetPosition, targetRotation)
                : EvaluateChase(view, ref _states[index], targetPosition, targetRotation, deltaTime, speed01,
                    Settings.BoostPullBack * Mathf.Max(_boost, 0f));
            pose.FieldOfView = Mathf.Lerp(view.FieldOfView, view.MaxFieldOfView, speed01) + Settings.BoostFieldOfView * _boost;
            pose.NearClip = view.NearClip;
            pose.Position += _lag * view.Inertia;
            pose.Rotation *= Noise(view, index);
            return pose;
        }

        /// <summary>Behind the chopper. Only orientation is smoothed: smoothing position against a moving target jitters.</summary>
        private static Pose EvaluateChase(CameraView view, ref ViewState state, Vector3 targetPosition, Quaternion targetRotation,
            float deltaTime, float speed01, float boostPullBack)
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
            var position = targetPosition + frame * (view.Offset * (1f + view.PullBack * speed01 + boostPullBack));
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

        private void UpdateSpeed(in CameraTarget target, float deltaTime, CameraSettings settingsAsset)
        {
            var targetPosition = target.Position;
            if (_hasLastTarget && Vector3.Distance(targetPosition, _lastTargetPosition) > TeleportDistance)
            {
                // Respawn or teleport: start every view fresh.
                for (var i = 0; i < _states.Length; i++) _states[i].Valid = false;
                _smoothedSpeed = 0f;
                _blend = 1f;
                _hasLastTarget = false;
                _hasLastVelocity = false;
                _lag = Vector3.zero;
                _acceleration = Vector3.zero;
            }

            if (deltaTime > 0f)
                _smoothedSpeed = Mathf.Lerp(_smoothedSpeed, target.Velocity.magnitude, 1f - Mathf.Exp(-settingsAsset.SpeedSmoothing * deltaTime));
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
            _effects?.Dispose();
            _effects = null;
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
