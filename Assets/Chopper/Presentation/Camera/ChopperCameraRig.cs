using UnityEngine;

namespace Chopper.Presentation.Camera
{
    /// <summary>
    /// Third-person chase camera. Follows the target's heading and pitch but never its roll, so banking does not
    /// spin the view. <see cref="LocalChopperCameraSystem"/> calls <see cref="Follow"/> once per frame, after the
    /// chopper's rendered pose is final, so the camera never lags a frame behind the chopper.
    /// </summary>
    [RequireComponent(typeof(UnityEngine.Camera))]
    public sealed class ChopperCameraRig : MonoBehaviour
    {
        [SerializeField] private Vector3 offset = new(0f, 5f, -16f);
        [SerializeField] private float lookAhead = 20f;
        [SerializeField, Tooltip("How fast the camera swings round to a new heading. Position stays locked to the chopper.")]
        private float headingSharpness = 6f;
        [SerializeField] private bool lockCursor = true;

        private bool _hasTarget;
        private Vector3 _smoothedForward = Vector3.forward;

        public void Follow(Vector3 targetPosition, Quaternion targetRotation, float deltaTime)
        {
            var forward = targetRotation * Vector3.forward;
            // Near-vertical noses have no stable heading; keep the last one.
            if (Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.98f)
                forward = _smoothedForward;

            if (!_hasTarget)
            {
                _hasTarget = true;
                _smoothedForward = forward;
            }
            else
            {
                // Smooth only the heading. Smoothing position against a moving target is what makes chase cams jitter.
                var t = 1f - Mathf.Exp(-headingSharpness * deltaTime);
                _smoothedForward = Vector3.Slerp(_smoothedForward, forward, t).normalized;
            }

            var frame = Quaternion.LookRotation(_smoothedForward, Vector3.up);
            var position = targetPosition + frame * offset;
            var lookPoint = targetPosition + _smoothedForward * lookAhead;
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookPoint - position, Vector3.up));
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
