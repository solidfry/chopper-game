using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Chopper.Simulation.Combat
{
    /// <summary>How a pylon weapon behaves. One system per behaviour, not per weapon: new weapons are mostly data.</summary>
    public enum PylonBehaviour : byte
    {
        /// <summary>Straight rockets, not ghosts (<see cref="RocketSystem"/>).</summary>
        Rocket = 0,
        /// <summary>Guided missile ghosts (<see cref="GuidedMissileSystem"/>).</summary>
        Guided = 1,
    }

    /// <summary>
    /// One weapon on a pylon, baked from the pylon module's weapon definition. The chopper has one entry per armed
    /// pylon; secondary fire uses the selected one (<see cref="PylonSelection"/>).
    /// </summary>
    [InternalBufferCapacity(4)]
    public struct PylonWeapon : IBufferElementData
    {
        public PylonBehaviour Behaviour;
        /// <summary>Short name for the HUD.</summary>
        public FixedString32Bytes Name;
        public float FireInterval;
        public float Speed;
        public float Lifetime;
        public float DirectDamage;
        public float SplashDamage;
        public float SplashRadius;
        public float3 MuzzleOffset;

        // Guided only.
        public SeekerType Seeker;
        public TargetClass Targets;
        public float LockConeCos;
        public float LockRange;
        public float LockTime;
        /// <summary>True: cannot fire without a lock (no dumb-fire).</summary>
        public bool RequiresLock;
        /// <summary>Radians per second.</summary>
        public float TurnRate;
        public float NavigationGain;
        public float ProximityFuse;
        /// <summary>Seconds after launch before the missile steers (it clears the launcher first).</summary>
        public float ArmTime;
        public Entity MissilePrefab;
        /// <summary>Guided only: splits into submunitions (<see cref="ClusterConfig.Count"/> 0 = no).</summary>
        public ClusterConfig Cluster;
        /// <summary>Guided only: missiles per trigger pull (swarm). 1 = one missile.</summary>
        public byte SalvoCount;
        /// <summary>Seconds between missiles of one salvo (one per tick at most, so predicted spawns match).</summary>
        public float SalvoInterval;
        /// <summary>Cosine of the launch spread cone of a salvo.</summary>
        public float SalvoConeCos;
        /// <summary>0..1: part of a salvo that flares do not fool (swarms saturate flares).</summary>
        public float FlareResistance;
    }

    /// <summary>
    /// Per-pylon cooldown, same order as <see cref="PylonWeapon"/>. Predicted on the owner. Timers that gate firing are
    /// sent unquantized: a rounded cooldown (0.00033 instead of 0) made the client's replay fire one tick later than its
    /// first prediction, and it drew the rocket twice.
    /// </summary>
    [InternalBufferCapacity(4)]
    public struct PylonWeaponState : IBufferElementData
    {
        [GhostField] public float CooldownRemaining;
        /// <summary>Missiles of the current salvo still to fire.</summary>
        [GhostField] public byte SalvoRemaining;
        [GhostField] public float SalvoTimer;
    }

    /// <summary>Salvo maths shared by the fire system and tests.</summary>
    public static class Salvo
    {
        /// <summary>
        /// True when missile <paramref name="shot"/> of a salvo ignores flares. Spread evenly through the salvo:
        /// resistance 0.3 of 10 missiles = shots 3, 6 and 9.
        /// </summary>
        public static bool IgnoresFlares(int shot, float resistance) =>
            math.floor((shot + 1) * resistance) > math.floor(shot * resistance);
    }

    /// <summary>Which pylon weapon secondary fire uses. Predicted on the owner; cycled by input.</summary>
    public struct PylonSelection : IComponentData
    {
        [GhostField] public byte Selected;
    }
}
