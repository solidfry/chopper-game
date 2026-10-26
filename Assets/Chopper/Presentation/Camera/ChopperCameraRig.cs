using UnityEngine;

namespace Chopper.Presentation.Camera
{
    /// <summary>
    /// Third-person chase camera. <see cref="LocalChopperCameraSystem"/> calls <see cref="Follow"/> once per frame,
    /// after the chopper's rendered pose is final, so the camera never lags a frame behind the chopper.
    /// Sense of speed: field of view and distance grow with speed; the camera rolls a little with the chopper.
    /// Hits taken shake the camera (<see cref="AddShake"/>).
    /// </summary>
    [RequireComponent(typeof(UnityEngine.Camera))]
    public sealed class ChopperCameraRig : MonoBehaviour
    {
        [Header("Framing")]
        [SerializeField] private Vector3 offset = new(0f, 4.5f, -15f);
        [SerializeField] private float lookAhead = 25f;
        [SerializeField, Tooltip("How fast the camera swings round to a new heading. Position stays locked to the chopper.")]
        private float headingSharpness = 8f;
        [SerializeField, Range(0f, 1f), Tooltip("How much of the chopper's roll the camera follows.")]
        private float rollFollow = 0.25f;

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

        [SerializeField] private bool lockCursor = true;

        private UnityEngine.Camera _camera;
        private bool _hasTarget;
        private Vector3 _smoothedForward = Vector3.forward;
        private Vector3 _smoothedUp = Vector3.up;
        private Vector3 _lastTargetPosition;
        private float _smoothedSpeed;
        private float _trauma;

        /// <summary>Adds camera shake. 0..1; trauma is capped at 1 and the shake grows with its square.</summary>
        public void AddShake(float trauma)
        {
            _trauma = Mathf.Clamp01(_trauma + trauma);
        }

        public void Follow(Vector3 targetPosition, Quaternion targetRotation, float deltaTime)
        {
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
                _smoothedUp = Vector3.Slerp(_smoothedUp, rolledUp, t).normalized;

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
        }

        private void OnEnable()
        {
            if (!lockCursor) return;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
