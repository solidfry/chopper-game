using UnityEngine;

namespace Chopper.Presentation.Effects
{
    /// <summary>Pure maths of one break-up piece (<see cref="WreckDebris"/>): launch, drag, bounce and trails.</summary>
    public static class DebrisMotion
    {
        /// <summary>Seconds a piece takes to sink out of sight at the end of its life.</summary>
        public const float SinkSeconds = 1.5f;
        /// <summary>Share of the wreck's velocity a piece keeps.</summary>
        public const float Inherit = 0.75f;
        /// <summary>Below this speed into the surface a bounce stops the piece.</summary>
        public const float RestSpeed = 1.5f;
        /// <summary>Seconds before a piece can come to rest.</summary>
        public const float SettleAfter = 0.4f;

        /// <summary>
        /// Start velocity: the wreck's, plus a push out from the blast (small pieces fly faster), tipped upward so the
        /// pieces arc. <paramref name="roll"/> is 0..1 random.
        /// </summary>
        public static Vector3 Launch(Vector3 wreckVelocity, Vector3 outward, float size, float roll)
        {
            var push = Mathf.Lerp(9f, 22f, roll) / Mathf.Clamp(size, 0.7f, 3f);
            var direction = (outward + Vector3.up * 0.6f).normalized;
            return wreckVelocity * Inherit + direction * push;
        }

        /// <summary>Spin (rad/s): small pieces tumble fast, big ones slowly.</summary>
        public static float SpinRate(float size, float roll) => Mathf.Lerp(3f, 12f, roll) / Mathf.Clamp(size, 0.8f, 4f);

        /// <summary>Air drag: small pieces slow more.</summary>
        public static Vector3 Drag(Vector3 velocity, float size, float deltaTime) =>
            velocity * Mathf.Exp(-0.25f / Mathf.Clamp(size, 0.5f, 4f) * deltaTime);

        /// <summary>Bounce off a surface: a little rebound along the normal, friction along it; slow hits rest.</summary>
        public static Vector3 Bounce(Vector3 velocity, Vector3 normal, out bool rest)
        {
            var into = Vector3.Dot(velocity, normal);
            if (into >= 0f)
            {
                rest = false;
                return velocity;
            }
            var along = velocity - normal * into;
            rest = -into < RestSpeed;
            return rest ? Vector3.zero : along * 0.55f - normal * (into * 0.3f);
        }

        /// <summary>Flame strength 0..1: full, then out over the last third of the burn.</summary>
        public static float Heat(float age, float burn) =>
            burn <= 0f || age >= burn ? 0f : Mathf.Clamp01((burn - age) / (burn * 0.35f));

        /// <summary>Smoke strength 0..1: thick at first, thinning to a wisp, none while it sinks.</summary>
        public static float Smoke(float age, float life) =>
            age >= life ? 0f : Mathf.Lerp(1f, 0.15f, Mathf.Clamp01(age / (life * 0.6f)));
    }
}
