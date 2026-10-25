using UnityEngine;

namespace Chopper.Presentation.Camera
{
    /// <summary>
    /// Third-person chase camera. Follows the target's heading and pitch but never its roll, so banking does not
    /// spin the view. <see cref="LocalChopperCameraSystem"/> feeds it the local player's chopper pose.
    /// </summary>
    [RequireComponent(typeof(UnityEngine.Camera))]
    public sealed class ChopperCameraRig : MonoBehaviour
    {
        [SerializeField] private Vector3 offset = new(0f, 5f, -16f);
        [SerializeField] private float lookAhead = 20f;
        [SerializeField] private float positionSharpness = 6f;
        [SerializeField] private float rotationSharpness = 8f;
        [SerializeField] private bool lockCursor = true;

        private bool _hasTarget;
        private Vector3 _targetPosition;
        private Vector3 _targetForward = Vector3.forward;

        public void SetTarget(Vector3 position, Quaternion rotation)
        {
            if (!_hasTarget)
            {
                _hasTarget = true;
                _targetPosition = position;
                _targetForward = rotation * Vector3.forward;
                SnapToTarget();
                return;
            }

            _targetPosition = position;
            _targetForward = rotation * Vector3.forward;
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

        private void LateUpdate()
        {
            if (!_hasTarget) return;

            var dt = Time.deltaTime;
            var (desiredPosition, desiredRotation) = DesiredPose();
            transform.position = Vector3.Lerp(transform.position, desiredPosition, 1f - Mathf.Exp(-positionSharpness * dt));
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, 1f - Mathf.Exp(-rotationSharpness * dt));
        }

        private void SnapToTarget()
        {
            var (position, rotation) = DesiredPose();
            transform.SetPositionAndRotation(position, rotation);
        }

        private (Vector3 position, Quaternion rotation) DesiredPose()
        {
            var forward = _targetForward.sqrMagnitude > 1e-4f ? _targetForward.normalized : Vector3.forward;
            // Near-vertical noses have no stable "up"; fall back to the last flat heading.
            if (Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.98f)
                forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;

            var frame = Quaternion.LookRotation(forward, Vector3.up);
            var position = _targetPosition + frame * offset;
            var lookPoint = _targetPosition + forward * lookAhead;
            return (position, Quaternion.LookRotation(lookPoint - position, Vector3.up));
        }
    }
}
