using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Chopper.Simulation.Combat
{
    /// <summary>
    /// Server only: a rocket in flight. Not a ghost: the path depends only on the launch (straight, or bent by
    /// <see cref="Gravity"/> and <see cref="Drag"/>), so clients draw it from the launch alone
    /// (<see cref="RocketLaunchRpc"/>, same maths: <see cref="RocketFlight"/>). No target rewind: others can dodge what
    /// they see. Bombs (gravity) and air mines (drag) are rockets with other numbers.
    /// </summary>
    public struct Rocket : IComponentData
    {
        public float3 Position;
        public float3 Velocity;
        public float Remaining;
        public Entity Shooter;
        public float DirectDamage;
        public float SplashDamage;
        public float SplashRadius;
        /// <summary>Downward acceleration, m/s2. 0 = flies straight.</summary>
        public float Gravity;
        /// <summary>Speed lost per second, as a rate (1 = about 63% in one second). 0 = keeps its speed.</summary>
        public float Drag;
        /// <summary>Explodes this close to a target of <see cref="FuseTargets"/>, metres. 0 = contact only.</summary>
        public float ProximityFuse;
        public TargetClass FuseTargets;
        /// <summary>Short name of the weapon that fired it, for the match log.</summary>
        public Unity.Collections.FixedString32Bytes Weapon;
    }

    /// <summary>Server to all clients: a rocket was launched. The shooter's client ignores its own (it predicted it).</summary>
    public struct RocketLaunchRpc : IRpcCommand
    {
        public float3 Position;
        public float3 Velocity;
        public float Lifetime;
        public float SplashRadius;
        public float Gravity;
        public float Drag;
        public float ProximityFuse;
        /// <summary><see cref="TargetClass"/> bits.</summary>
        public byte FuseTargets;
        public int ShooterNetworkId;
    }

    /// <summary>Client only, one entity per predicted launch, for presentation (draw the rocket at once).</summary>
    public struct LocalRocketLaunch : IComponentData
    {
        public float3 Position;
        public float3 Velocity;
        public float Lifetime;
        public float SplashRadius;
        public float Gravity;
        public float Drag;
        public float ProximityFuse;
        public TargetClass FuseTargets;
    }

    /// <summary>
    /// Rocket path maths shared by the server (<see cref="RocketSystem"/>), the client visuals, the lead indicator and
    /// tests. Pure, Burst friendly.
    /// </summary>
    public static class RocketFlight
    {
        /// <summary>One step: drag and gravity change the velocity first, then the rocket moves. Returns the new position.</summary>
        public static float3 Step(float3 position, ref float3 velocity, float gravity, float drag, float deltaTime)
        {
            if (drag > 0f) velocity *= math.exp(-drag * deltaTime);
            if (gravity > 0f) velocity.y -= gravity * deltaTime;
            return position + velocity * deltaTime;
        }

        /// <summary>True when the path bends or slows: aim helpers must step it, not use a straight line.</summary>
        public static bool IsBallistic(float gravity, float drag) => gravity > 0f || drag > 0f;

        /// <summary>
        /// Proximity fuse on one step from <paramref name="start"/> to <paramref name="end"/>: true when the path comes
        /// within <paramref name="fuse"/> of <paramref name="target"/>. <paramref name="point"/> = the closest point
        /// of the step (where it explodes).
        /// </summary>
        public static bool FuseHit(float3 start, float3 end, float3 target, float fuse, out float3 point)
        {
            point = end;
            if (fuse <= 0f) return false;
            var step = end - start;
            var lengthSq = math.lengthsq(step);
            var t = lengthSq > 1e-8f ? math.saturate(math.dot(target - start, step) / lengthSq) : 0f;
            point = start + step * t;
            return math.distancesq(point, target) <= fuse * fuse;
        }

        /// <summary>Launch direction of rocket <paramref name="shot"/> of a salvo: fanned in the pod's cone. Same on
        /// server and owner (the seed is the shooter).</summary>
        public static float3 SalvoDirection(float3 forward, byte salvoCount, float salvoConeCos, int shooterId, int shot) =>
            salvoCount > 1 && salvoConeCos < 1f
                ? Cluster.Direction(forward, salvoConeCos, (uint)shooterId * 7919u, shot)
                : forward;
    }
}
