using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Chopper.Simulation.Combat
{
    /// <summary>
    /// A ground gun turret (first use: guards for the Capture the Nuke pad). Server-run. It shoots the same server rounds
    /// as chopper guns (<see cref="ActiveBullet"/>), so hits, damage, falloff and the match log need no new path.
    /// When destroyed it is down for <see cref="RespawnSeconds"/>, then comes back at full health.
    /// </summary>
    public struct TurretConfig : IComponentData
    {
        /// <summary>The gun: fire rate, damage, range, falloff, bullet speed, muzzle (turret space), name.</summary>
        public HitscanWeaponConfig Gun;
        /// <summary>Trunnion to barrel tip, metres: rounds start here along the aim (server and tracers).</summary>
        public float BarrelLength;
        /// <summary>How fast the barrel turns, radians per second.</summary>
        public float TurnRate;
        /// <summary>Fires only when the barrel is within this angle of the lead point, radians.</summary>
        public float FireCone;
        /// <summary>Random aim error per shot, radians (so a turret is a threat, not a laser).</summary>
        public float Spread;
        public float RespawnSeconds;
        /// <summary>Team it belongs to. 0 = neutral: shoots every chopper.</summary>
        public byte Team;
        /// <summary>
        /// Health scales with the bigger team (owner, 20 Nov: "turret toughness should scale to the size of the teams"):
        /// this much per pilot, never below <see cref="MinHealth"/> nor above <see cref="MaxHealth"/>. 0 = always max.
        /// </summary>
        public float HealthPerPilot;
        public float MinHealth;
        public float MaxHealth;
    }

    /// <summary>Replicated so clients can turn the barrel, draw tracers and hide a destroyed turret.</summary>
    public struct TurretState : IComponentData
    {
        /// <summary>World direction of the barrel.</summary>
        [GhostField(Quantization = 1000)] public float3 Aim;
        /// <summary>Shots fired so far; clients draw a tracer each time it goes up (as <see cref="HitscanWeaponState"/>).</summary>
        [GhostField] public uint ShotCount;
        [GhostField] public bool Down;
        /// <summary>Seconds until it comes back (server).</summary>
        public float RespawnRemaining;
        public float Cooldown;
        public Entity Target;
        public Random Random;
    }

    /// <summary>The model's moving parts (children): head turns about its up axis, guns tilt about their right axis.</summary>
    public struct TurretRig : IComponentData
    {
        public Entity Head;
        public Entity Guns;
    }

    /// <summary>Pure turret rules, shared by the server system and EditMode tests.</summary>
    public static class TurretRules
    {
        /// <summary>A neutral turret (team 0) shoots everyone; a team turret shoots only other teams.</summary>
        public static bool IsEnemy(byte turretTeam, byte targetTeam) =>
            turretTeam == 0 || targetTeam != turretTeam;

        /// <summary>
        /// The rig angles for a world aim, in the turret's frame: yaw about its up axis (0 = its forward), pitch up from
        /// level (radians). A destroyed turret droops its guns (<paramref name="down"/>).
        /// </summary>
        public static void RigAngles(in float4x4 turretToWorld, float3 aim, bool down, out float yaw, out float pitch)
        {
            var local = math.mul(math.inverse(new float3x3(turretToWorld)), aim);
            yaw = math.atan2(local.x, local.z);
            pitch = down ? -DroopAngle : math.atan2(local.y, math.length(local.xz));
        }

        /// <summary>Gun tilt of a destroyed turret, radians below level.</summary>
        public const float DroopAngle = 0.35f;

        /// <summary>World muzzle: the gun's offset (the trunnion, turret space) plus the barrel along the aim.</summary>
        public static float3 Muzzle(in float4x4 turretToWorld, in TurretConfig config, float3 aim) =>
            math.transform(turretToWorld, config.Gun.MuzzleOffset) + aim * config.BarrelLength;

        /// <summary>Turns <paramref name="current"/> toward <paramref name="wanted"/> by at most <paramref name="maxAngle"/>.</summary>
        public static float3 Slew(float3 current, float3 wanted, float maxAngle)
        {
            current = math.normalizesafe(current, wanted);
            wanted = math.normalizesafe(wanted, current);
            var angle = Angle(current, wanted);
            if (angle <= maxAngle || angle < 1e-5f) return wanted;
            var axis = math.cross(current, wanted);
            if (math.lengthsq(axis) < 1e-10f)
                axis = math.abs(current.y) < 0.99f ? math.cross(current, math.up()) : math.cross(current, math.right());
            return math.normalize(math.rotate(quaternion.AxisAngle(math.normalize(axis), maxAngle), current));
        }

        public static float Angle(float3 a, float3 b) =>
            math.acos(math.clamp(math.dot(math.normalizesafe(a), math.normalizesafe(b)), -1f, 1f));

        /// <summary>Ready to fire: not cooling down and the barrel is inside the fire cone.</summary>
        public static bool CanFire(float cooldown, float3 aim, float3 wanted, float fireCone) =>
            cooldown <= 0f && Angle(aim, wanted) <= fireCone;

        /// <summary>The aim with a random error of up to <paramref name="spread"/> radians.</summary>
        public static float3 Scatter(float3 aim, float spread, ref Random random)
        {
            if (spread <= 0f) return aim;
            var side = math.normalizesafe(math.cross(aim, math.up()), math.right());
            var up = math.cross(side, aim);
            var angle = random.NextFloat(0f, spread);
            var turn = random.NextFloat(0f, 2f * math.PI);
            var offset = (side * math.cos(turn) + up * math.sin(turn)) * math.tan(angle);
            return math.normalize(aim + offset);
        }

        /// <summary>Counts the down time. True on the tick it comes back.</summary>
        /// <summary>Max health for <paramref name="pilots"/> in the bigger team (see <see cref="TurretConfig.HealthPerPilot"/>).</summary>
        public static float HealthFor(in TurretConfig config, int pilots)
        {
            if (config.HealthPerPilot <= 0f || pilots <= 0) return config.MaxHealth;
            return math.clamp(config.HealthPerPilot * pilots, config.MinHealth, config.MaxHealth);
        }

        public static bool TickRespawn(ref TurretState state, bool dead, float respawnSeconds, float deltaTime)
        {
            if (!dead) return false;
            if (!state.Down)
            {
                state.Down = true;
                state.RespawnRemaining = respawnSeconds;
                state.Target = Entity.Null;
                return false;
            }
            state.RespawnRemaining -= deltaTime;
            if (state.RespawnRemaining > 0f) return false;
            state.Down = false;
            state.RespawnRemaining = 0f;
            state.Cooldown = 0f;
            return true;
        }
    }
}
