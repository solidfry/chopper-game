using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Chopper.Simulation.Combat
{
    /// <summary>How a killed chopper goes down (Docs/ChopperDeathDesign.md). None = alive.</summary>
    public enum DeathStyle : byte
    {
        None = 0,
        /// <summary>Explodes at once (crash, air burst, too low to fall).</summary>
        Explode = 1,
        /// <summary>Tail rotor lost: fast flat spin round the mast.</summary>
        TailSpin = 2,
        /// <summary>Main rotor lost: no lift, slow tumble.</summary>
        RotorDrop = 3,
        /// <summary>Pilot hit: nose goes down, keeps its speed.</summary>
        NoseDive = 4,
        /// <summary>Engine hit or burning: slow spiral, long glide.</summary>
        EngineSpiral = 5,
    }

    /// <summary>
    /// A falling wreck. On the chopper prefab (ghost), <see cref="Style"/> None while alive. The server sets it when the
    /// chopper dies (<see cref="WreckSystem"/>); <see cref="WreckFlightSystem"/> flies it on server and clients.
    /// </summary>
    [GhostComponent]
    public struct Wreck : IComponentData
    {
        [GhostField] public DeathStyle Style;
        /// <summary>+1 or -1: the way it spins (from the hit side).</summary>
        [GhostField] public sbyte SpinSign;
        /// <summary>The player whose chopper it was (the owner is cleared on death): the kill cam follows it.</summary>
        [GhostField] public int OwnerNetworkId;
        /// <summary>Seconds since death. Predicted locally; the server's count decides the time-out.</summary>
        public float Age;

        public bool Active => Style != DeathStyle.None;
    }

    /// <summary>Server only: the hit that killed this chopper (<see cref="ApplyDamageSystem"/>).</summary>
    [GhostComponent(PrefabType = GhostPrefabType.Server)]
    public struct DeathBlow : IComponentData
    {
        public bool Set;
        public DamageKind Kind;
        /// <summary>Hit point in the chopper's own space (zero when the hit had no point).</summary>
        public float3 LocalPoint;
        public bool HasPoint;
        /// <summary>Killing blow divided by the health it had left (1 = just enough).</summary>
        public float Overkill;
    }

    /// <summary>Death style tuning (one per chopper; the same defaults for every chassis for now).</summary>
    public struct WreckConfig : IComponentData
    {
        /// <summary>An explosion this many times the health left blows it up in the air.</summary>
        public float InstantOverkill;
        /// <summary>Lower than this (m above the ground): no room to fall, explode at once.</summary>
        public float LowAltitude;
        /// <summary>A wreck explodes after this many seconds even without a contact.</summary>
        public float MaxSeconds;
        /// <summary>Velocity change (m/s in one tick) that counts as hitting something.</summary>
        public float ImpactChange;

        public static WreckConfig Default => new()
        {
            InstantOverkill = 3f,
            LowAltitude = 8f,
            MaxSeconds = 6f,
            ImpactChange = 4f,
        };
    }

    /// <summary>Death style choice and wreck motion: pure maths shared by the systems and the tests.</summary>
    public static class DeathStyles
    {
        /// <summary>Hit zones on the body, as a share of the collider's half size from its centre.</summary>
        public const float TailZone = -0.35f;
        public const float NoseZone = 0.45f;
        public const float TopZone = 0.45f;

        /// <param name="zone">Hit point relative to the collider box: -1..1 on each axis (x right, y up, z forward).</param>
        public static DeathStyle Pick(in WreckConfig config, in DeathBlow blow, float3 zone, float altitude)
        {
            if (blow.Kind is DamageKind.Impact or DamageKind.Landing or DamageKind.Toppled or DamageKind.Bounds)
                return DeathStyle.Explode;
            if (blow.Kind == DamageKind.Explosion && blow.Overkill >= config.InstantOverkill) return DeathStyle.Explode;
            if (altitude < config.LowAltitude) return DeathStyle.Explode;
            if (blow.Kind is DamageKind.Burn or DamageKind.Corrosion || !blow.HasPoint) return DeathStyle.EngineSpiral;
            if (zone.z < TailZone) return DeathStyle.TailSpin;
            if (zone.y > TopZone) return DeathStyle.RotorDrop;
            if (zone.z > NoseZone) return DeathStyle.NoseDive;
            return DeathStyle.EngineSpiral;
        }

        /// <summary>Spin direction: a hit on the right side turns it one way, on the left the other.</summary>
        public static sbyte SpinSign(float3 zone) => (sbyte)(zone.x >= 0f ? 1 : -1);

        /// <summary>
        /// The motion of a style at <paramref name="age"/> seconds: the spin it turns toward (rad/s, in its own space)
        /// and the share of gravity its rotor still carries.
        /// </summary>
        public static void Motion(DeathStyle style, float age, float sign, float noseDown, out float3 localSpin, out float lift)
        {
            switch (style)
            {
                case DeathStyle.TailSpin:
                    // The spin builds as the tail goes: about 1.5 to 6 rad/s.
                    localSpin = new float3(0f, sign * math.min(1.5f + age * 2f, 6f), sign * 0.3f);
                    lift = 0.55f;
                    break;
                case DeathStyle.RotorDrop:
                    localSpin = new float3(sign * 0.7f, 0f, 0.5f);
                    lift = 0f;
                    break;
                case DeathStyle.NoseDive:
                    // Pitch down until the nose points well down, then hold.
                    localSpin = new float3(noseDown < 0.75f ? 0.9f : 0f, 0f, sign * 0.2f);
                    lift = 0.25f;
                    break;
                case DeathStyle.EngineSpiral:
                    localSpin = new float3(0f, sign * 1.2f, sign * 0.35f);
                    lift = 0.7f;
                    break;
                default:
                    localSpin = float3.zero;
                    lift = 0f;
                    break;
            }
        }
    }

    /// <summary>Server to all clients: a wreck exploded here (the ghost is destroyed at the same time).</summary>
    public struct WreckExplodedRpc : IRpcCommand
    {
        public float3 Position;
        public float3 Velocity;
        public DeathStyle Style;
        public int OwnerNetworkId;
    }
}
