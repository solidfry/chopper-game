using Chopper.Simulation.Combat;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Chopper.Simulation.Session
{
    public enum MatchLogKind : byte
    {
        /// <summary>A gun round, rocket or missile left the chopper.</summary>
        Shot = 0,
        /// <summary>Damage applied (any source, also crashes and bounds).</summary>
        Hit = 1,
        /// <summary>A chopper died. <see cref="MatchLogEntry.Actor"/> is the credited killer (0 = none).</summary>
        Kill = 2,
        Flare = 3,
        /// <summary>A heat seeker lost its target to flares.</summary>
        MissileDecoyed = 4,
        /// <summary>A missile detonated or ran out; <see cref="MatchLogEntry.Detail"/> is a <see cref="MissileOutcome"/>.</summary>
        MissileEnd = 5,
        /// <summary>A field repair ended; <see cref="MatchLogEntry.Amount"/> is the health it mended.</summary>
        Repair = 6,
        /// <summary>The actor started their pilot ability; <see cref="MatchLogEntry.Detail"/> is its <see cref="AbilityKind"/>.</summary>
        Ability = 7,
    }

    public enum MissileOutcome : byte
    {
        /// <summary>Hit something (a chopper or the world).</summary>
        Impact = 0,
        Proximity = 1,
        Expired = 2,
    }

    /// <summary>
    /// Server only. One gameplay event for the match log. Simulation systems (Burst) append to the singleton buffer when
    /// it exists (<c>SystemAPI.TryGetSingletonBuffer</c>). <see cref="Match.MatchStatsSystem"/> makes it on every server,
    /// counts the match stats from it and clears it each frame; <see cref="MatchLogSystem"/> (when logging is on) reads it
    /// first and writes the entries as JSON lines. Network ids: 0 = no player (environment, training
    /// target).
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct MatchLogEntry : IBufferElementData
    {
        public MatchLogKind Kind;
        /// <summary>Server elapsed time (s) when it happened.</summary>
        public double Time;
        public int Actor;
        public int Target;
        /// <summary>Weapon short name (module display name, upper case); empty for crashes, bounds, flares.</summary>
        public FixedString32Bytes Weapon;
        public DamageKind Damage;
        public float Amount;
        /// <summary>Metres between actor and target (or the missile and its target); -1 = unknown.</summary>
        public float Distance;
        public float3 ActorPosition;
        public float3 TargetPosition;
        /// <summary>Hit: 1 = this hit killed. MissileEnd: a <see cref="MissileOutcome"/>.</summary>
        public byte Detail;
        /// <summary>Hit: the victim's health after it.</summary>
        public float HealthAfter;
    }
}
