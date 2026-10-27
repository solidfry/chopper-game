using UnityEngine;

namespace Chopper.Presentation.Bridge
{
    /// <summary>
    /// Velocity from how far a rendered position really moved, smoothed. For the HUD and camera: the physics velocity can
    /// grow while the chopper is blocked (boosting into the ground or a wall), but the player must see real movement.
    /// A jump faster than <see cref="MaxSpeed"/> is a respawn or teleport: it resets instead of showing a spike.
    /// </summary>
    public struct MeasuredVelocity
    {
        /// <summary>Faster than this between two frames (m/s) is a teleport, not flight.</summary>
        public const float MaxSpeed = 600f;
        /// <summary>How fast the value follows (1/s). The rendered position already moves smoothly; this only hides frame-time noise.</summary>
        private const float Sharpness = 12f;

        private Vector3 _lastPosition;
        private bool _hasLast;

        public Vector3 Velocity { get; private set; }
        public float Speed => Velocity.magnitude;

        public void Update(Vector3 position, float deltaTime)
        {
            if (deltaTime <= 0f) return;
            if (_hasLast)
            {
                var raw = (position - _lastPosition) / deltaTime;
                if (raw.magnitude > MaxSpeed)
                    Velocity = Vector3.zero;
                else
                    Velocity = Vector3.Lerp(Velocity, raw, 1f - Mathf.Exp(-Sharpness * deltaTime));
            }
            _lastPosition = position;
            _hasLast = true;
        }

        public void Reset()
        {
            _hasLast = false;
            Velocity = Vector3.zero;
        }
    }
}
