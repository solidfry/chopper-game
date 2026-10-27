using Unity.Mathematics;
using Unity.NetCode;

namespace Chopper.Simulation.Combat
{
    /// <summary>
    /// Cluster payload of a guided missile (plan section 3b): it flies to a split point, then releases
    /// <see cref="Count"/> submunitions in a cone. Submunitions are server <see cref="Rocket"/>s (not ghosts); clients
    /// draw them from one <see cref="ClusterSplitRpc"/> and its seed. <see cref="Count"/> 0 = not a cluster.
    /// </summary>
    public struct ClusterConfig
    {
        public byte Count;
        /// <summary>Splits this close to the target, metres.</summary>
        public float SplitDistance;
        /// <summary>Splits this long after launch at the latest (dumb-fire, or no target), seconds.</summary>
        public float SplitTime;
        /// <summary>Submunition speed, m/s.</summary>
        public float Speed;
        /// <summary>Cosine of the spread cone half-angle.</summary>
        public float ConeCos;
        public float Lifetime;
        public float DirectDamage;
        public float SplashDamage;
        public float SplashRadius;

        public bool IsCluster => Count > 0;
    }

    /// <summary>Server to all: a cluster missile split here. Clients rebuild each submunition with <see cref="Cluster.Direction"/>.</summary>
    public struct ClusterSplitRpc : IRpcCommand
    {
        public float3 Position;
        /// <summary>The carrier's heading at the split; the cone is round it.</summary>
        public float3 Forward;
        public float Speed;
        public float ConeCos;
        public float Lifetime;
        public float SplashRadius;
        public uint Seed;
        public byte Count;
        public int ShooterNetworkId;
    }

    /// <summary>Cluster maths shared by the server (real submunitions), clients (visuals) and tests. Burst friendly.</summary>
    public static class Cluster
    {
        /// <summary>Most submunitions per missile (one RPC rebuilds them all).</summary>
        public const int MaxCount = 32;

        /// <summary>Direction of submunition <paramref name="index"/>: spread evenly over the cone round <paramref name="forward"/>.
        /// Same seed and index = same direction on every machine.</summary>
        public static float3 Direction(float3 forward, float coneCos, uint seed, int index)
        {
            var random = Random.CreateFromIndex(seed + (uint)index);
            // Uniform over the cone's cap: cos θ uniform in [coneCos, 1].
            var cosTheta = math.lerp(math.clamp(coneCos, -1f, 1f), 1f, random.NextFloat());
            var sinTheta = math.sqrt(math.max(0f, 1f - cosTheta * cosTheta));
            var phi = random.NextFloat(0f, 2f * math.PI);
            var local = new float3(sinTheta * math.cos(phi), sinTheta * math.sin(phi), cosTheta);

            var heading = math.normalizesafe(forward, math.forward());
            var up = math.abs(math.dot(heading, math.up())) > 0.99f ? math.right() : math.up();
            return math.mul(quaternion.LookRotation(heading, up), local);
        }

        /// <param name="splitRemaining">Time left to the timed split.</param>
        /// <param name="targetDistance">Distance to the target; below 0 = no target.</param>
        public static bool ShouldSplit(in ClusterConfig config, float splitRemaining, float targetDistance) =>
            config.IsCluster && (splitRemaining <= 0f || (targetDistance >= 0f && targetDistance <= config.SplitDistance));
    }
}
