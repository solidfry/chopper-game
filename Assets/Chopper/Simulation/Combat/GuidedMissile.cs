using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Chopper.Simulation.Combat
{
    /// <summary>
    /// A guided missile ghost. The server owns and flies it; the shooter predicts its own copy (predicted spawn), so the
    /// launch is instant; others see it interpolated. Fields that steering needs are ghost fields, so any copy can fly.
    /// </summary>
    public struct GuidedMissile : IComponentData
    {
        [GhostField] public Entity Target;
        [GhostField(Quantization = 100)] public float3 Velocity;
        [GhostField(Quantization = 10)] public float Speed;
        [GhostField(Quantization = 1000)] public float TurnRate;
        [GhostField(Quantization = 100)] public float NavigationGain;
        [GhostField(Quantization = 100)] public float Remaining;
        [GhostField(Quantization = 100)] public float ArmRemaining;
        public SeekerType Seeker;

        // Server only (damage is decided there).
        public Entity Shooter;
        public float ProximityFuse;
        public float DirectDamage;
        public float SplashDamage;
        public float SplashRadius;
    }

    /// <summary>Server to all: a missile exploded here (its ghost is removed, so clients need the place).</summary>
    public struct MissileDetonatedRpc : IRpcCommand
    {
        public float3 Position;
    }

    /// <summary>Steering maths shared by the missile system and tests.</summary>
    public static class Guidance
    {
        /// <summary>
        /// Lead pursuit: aim at where the target will be when the missile arrives (scaled by the navigation gain; 3 =
        /// full lead), then turn the velocity towards it by at most <paramref name="turnRate"/> × dt. Speed is kept.
        /// </summary>
        public static float3 Steer(float3 position, float3 velocity, float3 targetPosition, float3 targetVelocity,
            float speed, float turnRate, float navigationGain, float deltaTime)
        {
            var toTarget = targetPosition - position;
            var timeToGo = math.length(toTarget) / math.max(speed, 1f);
            var lead = math.saturate(navigationGain / 3f) * timeToGo;
            var aim = math.normalizesafe(toTarget + targetVelocity * lead, math.normalizesafe(velocity));

            var heading = math.normalizesafe(velocity, aim);
            var angle = math.acos(math.clamp(math.dot(heading, aim), -1f, 1f));
            var maxTurn = turnRate * deltaTime;
            if (angle <= maxTurn || angle < 1e-5f) return aim * speed;

            // Rotate the heading towards the aim by maxTurn around their common axis.
            var axis = math.normalizesafe(math.cross(heading, aim), math.up());
            var turned = math.mul(quaternion.AxisAngle(axis, maxTurn), heading);
            return math.normalizesafe(turned) * speed;
        }
    }
}
