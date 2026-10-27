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
    }

    /// <summary>Per-pylon cooldown, same order as <see cref="PylonWeapon"/>. Predicted on the owner.</summary>
    [InternalBufferCapacity(4)]
    public struct PylonWeaponState : IBufferElementData
    {
        [GhostField(Quantization = 1000)] public float CooldownRemaining;
    }

    /// <summary>Which pylon weapon secondary fire uses. Predicted on the owner; cycled by input.</summary>
    public struct PylonSelection : IComponentData
    {
        [GhostField] public byte Selected;
    }
}
