using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Chopper.Simulation.Combat
{
    /// <summary>What a seeker can lock on to.</summary>
    [Flags]
    public enum TargetClass : byte
    {
        None = 0,
        Air = 1,
        Ground = 2,
        All = Air | Ground,
    }

    public enum SeekerType : byte
    {
        /// <summary>Not a guided weapon: no lock.</summary>
        None = 0,
        /// <summary>Locks the hottest target in the cone; fooled by flares.</summary>
        Heat = 1,
    }

    /// <summary>Anything a seeker can lock: choppers (air), ground targets, later turrets, bases and the nuke.</summary>
    public struct Targetable : IComponentData
    {
        public TargetClass Class;
        /// <summary>Heat at rest. Speed and dash add to it (<see cref="Seeker.Heat"/>).</summary>
        public float BaseHeat;
    }

    /// <summary>
    /// The lock of this chopper's selected pylon weapon. Only the server writes it (<see cref="TargetLockSystem"/>);
    /// the owner gets it for the HUD.
    /// </summary>
    public struct TargetLock : IComponentData
    {
        [GhostField] public Entity Target;
        /// <summary>0 = just started, 1 = locked.</summary>
        [GhostField(Quantization = 100)] public float Progress;
        [GhostField] public bool Locked;
    }

    /// <summary>Server-written warnings for this chopper's pilot: how many enemies are locking it, and how many have a lock.</summary>
    public struct ThreatState : IComponentData
    {
        [GhostField] public byte Locking;
        [GhostField] public byte LockedBy;
    }

    /// <summary>Seeker maths shared by the lock system and tests. Pure functions, Burst friendly.</summary>
    public static class Seeker
    {
        /// <summary>Heat per m/s of speed (fast choppers glow brighter to a seeker).</summary>
        public const float HeatPerSpeed = 0.01f;
        /// <summary>Extra heat right after a dash (engine at full power).</summary>
        public const float DashHeat = 0.5f;

        public static float Heat(in Targetable target, float speed, bool dashing) =>
            target.BaseHeat + speed * HeatPerSpeed + (dashing ? DashHeat : 0f);

        /// <summary>True when the target is inside the seeker cone and range.</summary>
        /// <param name="coneCos">Cosine of the cone half-angle.</param>
        /// <param name="alignment">Cosine of the angle off the seeker axis (1 = dead ahead).</param>
        public static bool InCone(float3 origin, float3 forward, float3 target, float coneCos, float range,
            out float distance, out float alignment)
        {
            var offset = target - origin;
            distance = math.length(offset);
            alignment = distance > 1e-3f ? math.dot(offset / distance, forward) : 1f;
            return distance <= range && alignment >= coneCos;
        }

        /// <summary>Higher = better target: hot, near the centre of the cone, close.</summary>
        public static float Score(float heat, float alignment, float distance) =>
            heat * alignment * alignment / math.max(distance, 1f);
    }
}
