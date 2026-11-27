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
        /// <summary>Server: the shooter at launch, for kill credit if its chopper is gone at impact.</summary>
        public AttackerSnapshot Attacker;
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
        /// <summary>The chopper this round was aimed at (see <see cref="RocketEvade"/>); Null until one is.</summary>
        public Entity Threat;
        /// <summary>The evade for <see cref="Threat"/> was given (once per round).</summary>
        public bool EvadeDone;
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
        /// <summary>Pylon module id, for the client's look and sound (<see cref="PylonWeapon.ModuleId"/>).</summary>
        public ushort ModuleId;
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
        public ushort ModuleId;
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

    /// <summary>
    /// "Evaded" for rockets, bombs and air mines: a round that was aimed at a chopper (a moving round inside a narrow
    /// cone, or a hanging mine with the chopper close) and then flew past it, burst out of reach of it, or was left
    /// behind. One evade per round. Server only (<see cref="RocketSystem"/>), counted like an evaded missile.
    /// </summary>
    public static class RocketEvade
    {
        /// <summary>A moving round is aimed at a chopper inside this cone half-angle (cos 20°) and range.</summary>
        public const float AimCos = 0.94f;
        public const float AimRange = 120f;
        /// <summary>A round slower than this hangs (air mine).</summary>
        public const float HangSpeed = 5f;
        /// <summary>Passing within this many metres and flying on counts as a near miss.</summary>
        public const float NearMiss = 10f;
        /// <summary>A burst this far outside the splash still counts as close (beyond it, it was nowhere near).</summary>
        public const float BurstMargin = 15f;

        /// <summary>The round threatens the target: aimed at it (moving), or a hanging mine with the target near.</summary>
        public static bool Threatens(float3 position, float3 velocity, float3 target, float fuse, float splash)
        {
            var to = target - position;
            var distance = math.length(to);
            var speed = math.length(velocity);
            if (speed < HangSpeed) return distance <= 2f * math.max(fuse, splash);
            if (distance < 1e-3f || distance > AimRange) return false;
            return math.dot(velocity / speed, to / distance) >= AimCos;
        }

        /// <summary>This step passed the target within <see cref="NearMiss"/> and now flies away from it.</summary>
        public static bool PassedBy(float3 start, float3 end, float3 target)
        {
            var path = end - start;
            var length = math.lengthsq(path);
            var t = length > 1e-6f ? math.saturate(math.dot(target - start, path) / length) : 0f;
            var closest = start + path * t;
            return math.distance(closest, target) <= NearMiss && math.dot(path, target - end) < 0f;
        }

        /// <summary>A hanging mine the target got clear of without setting it off.</summary>
        public static bool LeftBehind(float3 position, float3 velocity, float3 target, float fuse, float splash) =>
            math.length(velocity) < HangSpeed && math.distance(position, target) > 3f * math.max(fuse, splash);

        /// <summary>The round burst near the target but its splash missed.</summary>
        public static bool BurstMissed(float3 burst, float3 target, float splash)
        {
            var distance = math.distance(burst, target);
            return distance > splash && distance <= splash + BurstMargin;
        }
    }
}
