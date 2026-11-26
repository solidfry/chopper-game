using Unity.Mathematics;

namespace Chopper.Simulation.Combat
{
    /// <summary>
    /// How a guided missile leaves the pylon and curves to its target (owner, 26 Nov: "arch in a curve more ... almost
    /// like an Evangelion style movement"): it kicks out up and to the pylon's side at part speed, speeds up, then
    /// weaves round the line to the target in a slow corkscrew that tightens as it closes. Baked from
    /// <see cref="Definitions.GuidedMissileDefinition"/>. All zero = the old straight launch and pure lead pursuit.
    /// </summary>
    public struct MissileArc
    {
        /// <summary>Launch direction turned this far (radians) from the nose, up and out to the pylon side.</summary>
        public float KickRadians;
        /// <summary>Launch speed as a share of full speed (0..1). 0 or 1 = full speed at once.</summary>
        public float StartSpeedShare;
        /// <summary>Seconds from launch to full speed.</summary>
        public float BoostSeconds;
        /// <summary>Corkscrew size: the aim point circles the target at this angle (radians) seen from the missile.</summary>
        public float WeaveRadians;
        /// <summary>Corkscrew turns per second.</summary>
        public float WeaveHz;
    }

    /// <summary>Arc maths shared by the launch, the flight system and tests. Stateless: everything follows from the age.</summary>
    public static class MissileArcMath
    {
        /// <summary>Within this distance of the target the corkscrew fades out, metres (it homes straight at the end).</summary>
        public const float WeaveFadeStart = 160f;
        public const float WeaveFadeEnd = 35f;

        /// <summary>
        /// Launch direction: <paramref name="forward"/> turned by the kick, towards up plus the pylon side
        /// (<paramref name="side"/>: +1 right pylon, -1 left, 0 centre: straight up). A salvo shot turns the side round the
        /// nose a little more for each shot, so a swarm fans out like a flower.
        /// </summary>
        public static float3 KickDirection(float3 forward, float3 up, float3 right, float side, float kickRadians, int shot)
        {
            if (kickRadians <= 0f) return forward;
            // Roll angle of the kick round the nose, from straight up: +45 degrees to the right pylon's side, -45 left.
            // Salvo shots swing it in golden-angle steps, kept within 80 degrees of up so no missile dives at the ground.
            var roll = math.sign(side) * math.radians(45f);
            if (shot > 0) roll += math.sin(math.radians(137.5f) * shot) * math.radians(70f);
            roll = math.clamp(roll, -math.radians(80f), math.radians(80f));
            var lift = math.normalizesafe(up - forward * math.dot(up, forward), math.up());
            var side90 = math.normalizesafe(right - forward * math.dot(right, forward), math.right());
            var outward = lift * math.cos(roll) + side90 * math.sin(roll);
            return math.normalizesafe(forward * math.cos(kickRadians) + outward * math.sin(kickRadians), forward);
        }

        /// <summary>Speed at this age: from the start share up to full over the boost time, easing in like a motor lighting.</summary>
        public static float SpeedAt(in MissileArc arc, float fullSpeed, float age)
        {
            if (arc.BoostSeconds <= 0f || arc.StartSpeedShare <= 0f || arc.StartSpeedShare >= 1f) return fullSpeed;
            var t = math.saturate(age / arc.BoostSeconds);
            return fullSpeed * math.lerp(arc.StartSpeedShare, 1f, t * t * (3f - 2f * t));
        }

        /// <summary>
        /// Where to steer instead of the target: the target plus a point circling it, square to the line of sight, at
        /// <see cref="MissileArc.WeaveRadians"/> seen from the missile. Fades to the target itself near in.
        /// </summary>
        public static float3 WeaveAim(in MissileArc arc, float3 position, float3 target, float age, float phase)
        {
            if (arc.WeaveRadians <= 0f || arc.WeaveHz <= 0f) return target;
            var toTarget = target - position;
            var distance = math.length(toTarget);
            if (distance < 1e-3f) return target;
            var line = toTarget / distance;
            var across = math.normalizesafe(math.cross(line, math.up()), math.right());
            var lift = math.cross(across, line);
            var fade = math.saturate((distance - WeaveFadeEnd) / (WeaveFadeStart - WeaveFadeEnd));
            var angle = phase + 2f * math.PI * arc.WeaveHz * age;
            var radius = math.tan(arc.WeaveRadians) * distance * fade;
            return target + (across * math.cos(angle) + lift * math.sin(angle)) * radius;
        }

        /// <summary>Corkscrew start angle per missile, so a salvo does not weave in step.</summary>
        public static float Phase(int shooterId, int shot) => math.frac(shooterId * 0.618034f + shot * 0.381966f) * 2f * math.PI;
    }
}
