using UnityEngine;
using UnityEngine.InputSystem;

namespace Chopper.Presentation.Camera
{
    /// <summary>
    /// Third-person chase camera. <see cref="LocalChopperCameraSystem"/> calls <see cref="Follow"/> once per frame,
    /// after the chopper's rendered pose is final, so the camera never lags a frame behind the chopper.
    /// Sense of speed: field of view and distance grow with speed; the camera rolls with the chopper on a short delay.
    /// Hits taken shake the camera (<see cref="AddShake"/>).
    /// C (gamepad Y) switches to the cockpit view, like the old game's camera switch: fixed to the chopper at the pilot's
    /// seat, looking at the gun's aim point, with a light wobble. The choice is saved.
    /// </summary>
    [RequireComponent(typeof(UnityEngine.Camera))]
    public sealed class ChopperCameraRig : MonoBehaviour
    {
        [Header("Framing")]
        [SerializeField] private Vector3 offset = new(0f, 4.5f, -15f);
        [SerializeField] private float lookAhead = 25f;
        [SerializeField, Tooltip("How fast the camera swings round to a new heading. Position stays locked to the chopper.")]
        private float headingSharpness = 8f;
        [SerializeField, Range(0f, 1f), Tooltip("How much of the chopper's roll the camera follows. 1 = all of it.")]
        private float rollFollow = 0.85f;
        [SerializeField, Tooltip("How fast the camera catches up with the chopper's roll. Lower = more delay (4 = about a quarter second).")]
        private float rollSharpness = 4f;

        [Header("Sense of speed")]
        [SerializeField] private float baseFieldOfView = 65f;
        [SerializeField] private float maxFieldOfView = 85f;
        [SerializeField, Tooltip("Extra distance at top speed, as a fraction of the offset.")]
        private float maxPullBack = 0.3f;
        [SerializeField, Tooltip("Speed (m/s) at which the speed effects are at full strength.")]
        private float fullEffectSpeed = 80f;
        [SerializeField] private float speedSmoothing = 3f;

        [Header("Shake (hits taken)")]
        [SerializeField, Tooltip("Largest shake angle in degrees, at full trauma.")]
        private float maxShakeDegrees = 4f;
        [SerializeField, Tooltip("Trauma lost per second.")]
        private float shakeRecovery = 1.5f;
        [SerializeField] private float shakeFrequency = 18f;

        [Header("Cockpit view (C / gamepad Y)")]
        [SerializeField, Tooltip("Pilot's eye in chopper space. From the old game's cockpit camera.")]
        private Vector3 cockpitOffset = new(0f, -0.51f, 2.47f);
        [SerializeField, Tooltip("Where the pilot looks, chopper space (the old ReticalPosition).")]
        private Vector3 cockpitLookPoint = new(0f, -1.5f, 79f);
        [SerializeField] private float cockpitFieldOfView = 70f;
        [SerializeField, Tooltip("Engine wobble, degrees.")] private float cockpitWobble = 0.25f;
        [SerializeField] private float cockpitWobbleFrequency = 6f;

        [SerializeField] private bool lockCursor = true;

        private UnityEngine.Camera _camera;
        private bool _hasTarget;
        private Vector3 _smoothedForward = Vector3.forward;
        private Vector3 _smoothedUp = Vector3.up;
        private Vector3 _lastTargetPosition;
        private float _smoothedSpeed;
        private float _trauma;
        private bool _cockpit;
        /// <summary>A menu is open (<see cref="SetCursorFree"/>): no view switching.</summary>
        private bool _menuOpen;

        private const string CockpitKey = "Chopper.Camera.Cockpit";

        /// <summary>True while the view is inside the cockpit.</summary>
        public bool IsCockpit => _cockpit;

        /// <summary>Switches between chase and cockpit view (saved).</summary>
        public void SetCockpit(bool cockpit)
        {
            _cockpit = cockpit;
            PlayerPrefs.SetInt(CockpitKey, cockpit ? 1 : 0);
            // The chase camera snaps to the chopper again instead of swinging in from the old view.
            _hasTarget = false;
        }

        /// <summary>Adds camera shake. 0..1; trauma is capped at 1 and the shake grows with its square.</summary>
        public void AddShake(float trauma)
        {
            _trauma = Mathf.Clamp01(_trauma + trauma);
        }

        public void Follow(Vector3 targetPosition, Quaternion targetRotation, float deltaTime)
        {
            if (_cockpit)
            {
                FollowCockpit(targetPosition, targetRotation, deltaTime);
                return;
            }

            var forward = targetRotation * Vector3.forward;
            // Near-vertical noses have no stable heading; keep the last one.
            if (Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.98f)
                forward = _smoothedForward;
            var rolledUp = Vector3.Slerp(Vector3.up, targetRotation * Vector3.up, rollFollow);

            if (!_hasTarget)
            {
                _hasTarget = true;
                _smoothedForward = forward;
                _smoothedUp = rolledUp;
                _lastTargetPosition = targetPosition;
            }
            else if (deltaTime > 0f)
            {
                // Smooth only orientation. Smoothing position against a moving target is what makes chase cams jitter.
                var t = 1f - Mathf.Exp(-headingSharpness * deltaTime);
                _smoothedForward = Vector3.Slerp(_smoothedForward, forward, t).normalized;
                // Roll follows on its own, slower curve: the horizon tilts with the chopper a moment later.
                _smoothedUp = Vector3.Slerp(_smoothedUp, rolledUp, 1f - Mathf.Exp(-rollSharpness * deltaTime)).normalized;

                var speed = (targetPosition - _lastTargetPosition).magnitude / deltaTime;
                _smoothedSpeed = Mathf.Lerp(_smoothedSpeed, speed, 1f - Mathf.Exp(-speedSmoothing * deltaTime));
                _lastTargetPosition = targetPosition;
            }

            var speed01 = Mathf.Clamp01(_smoothedSpeed / fullEffectSpeed);
            _camera.fieldOfView = Mathf.Lerp(baseFieldOfView, maxFieldOfView, speed01);

            var frame = Quaternion.LookRotation(_smoothedForward, _smoothedUp);
            var position = targetPosition + frame * (offset * (1f + maxPullBack * speed01));
            var lookPoint = targetPosition + _smoothedForward * lookAhead;
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookPoint - position, _smoothedUp) * Shake(deltaTime));
        }

        /// <summary>Rigid to the chopper: the horizon tilts and turns with it, as from the pilot's seat.</summary>
        private void FollowCockpit(Vector3 targetPosition, Quaternion targetRotation, float deltaTime)
        {
            _camera.fieldOfView = cockpitFieldOfView;
            var eye = targetPosition + targetRotation * cockpitOffset;
            var look = targetRotation * Quaternion.LookRotation(cockpitLookPoint - cockpitOffset, Vector3.up);
            var t = Time.time * cockpitWobbleFrequency;
            var wobble = Quaternion.Euler(
                cockpitWobble * (Mathf.PerlinNoise(t, 0.4f) * 2f - 1f),
                cockpitWobble * (Mathf.PerlinNoise(0.9f, t) * 2f - 1f),
                0f);
            transform.SetPositionAndRotation(eye, look * wobble * Shake(deltaTime));
        }

        private void Update()
        {
            var keyboard = Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame;
            var gamepad = Gamepad.current != null && Gamepad.current.buttonNorth.wasPressedThisFrame;
            if ((keyboard || gamepad) && !_menuOpen) SetCockpit(!_cockpit);
        }

        private Quaternion Shake(float deltaTime)
        {
            if (_trauma <= 0f) return Quaternion.identity;
            _trauma = Mathf.Max(0f, _trauma - shakeRecovery * deltaTime);

            var amount = _trauma * _trauma * maxShakeDegrees;
            var t = Time.time * shakeFrequency;
            return Quaternion.Euler(
                amount * (Mathf.PerlinNoise(t, 0.1f) * 2f - 1f),
                amount * (Mathf.PerlinNoise(0.3f, t) * 2f - 1f),
                amount * 0.5f * (Mathf.PerlinNoise(t, 0.7f) * 2f - 1f));
        }

        private void Awake()
        {
            _camera = GetComponent<UnityEngine.Camera>();
            _cockpit = PlayerPrefs.GetInt(CockpitKey, 0) != 0;
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
