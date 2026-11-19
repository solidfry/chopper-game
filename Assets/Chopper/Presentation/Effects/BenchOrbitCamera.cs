using UnityEngine;
using UnityEngine.InputSystem;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Effects bench camera: orbits <see cref="target"/>. Right mouse drag turns, wheel zooms, middle drag moves the
    /// look point up and down; F1-F4 jump to set views (side at chopper height, above like a chopper, low, close).
    /// </summary>
    public sealed class BenchOrbitCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private float distance = 60f;
        [SerializeField] private float yaw = 30f;
        [SerializeField] private float pitch = 15f;
        [SerializeField] private float lookHeight = 8f;

        private const float TurnSpeed = 0.25f;
        private const float ZoomStep = 0.1f;

        /// <summary>Editor only: the scene builder links the look point.</summary>
        public void Configure(Transform lookAt) => target = lookAt;

        private void LateUpdate()
        {
            var mouse = Mouse.current;
            if (mouse != null)
            {
                var delta = mouse.delta.ReadValue();
                if (mouse.rightButton.isPressed)
                {
                    yaw += delta.x * TurnSpeed;
                    pitch = Mathf.Clamp(pitch - delta.y * TurnSpeed, -10f, 85f);
                }
                if (mouse.middleButton.isPressed) lookHeight = Mathf.Clamp(lookHeight + delta.y * 0.05f, 0f, 80f);
                var scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f) distance = Mathf.Clamp(distance * (1f - Mathf.Sign(scroll) * ZoomStep), 5f, 400f);
            }
            var keys = Keyboard.current;
            if (keys != null)
            {
                if (keys.f1Key.wasPressedThisFrame) View(60f, 30f, 8f, 8f);
                if (keys.f2Key.wasPressedThisFrame) View(90f, 30f, 45f, 8f);
                if (keys.f3Key.wasPressedThisFrame) View(35f, 30f, 3f, 6f);
                if (keys.f4Key.wasPressedThisFrame) View(20f, 30f, 12f, 4f);
            }

            var centre = (target != null ? target.position : Vector3.zero) + Vector3.up * lookHeight;
            var rotation = Quaternion.Euler(pitch, yaw, 0f);
            transform.SetPositionAndRotation(centre - rotation * Vector3.forward * distance, rotation);
        }

        private void View(float viewDistance, float viewYaw, float viewPitch, float viewHeight)
        {
            distance = viewDistance;
            yaw = viewYaw;
            pitch = viewPitch;
            lookHeight = viewHeight;
        }
    }
}
