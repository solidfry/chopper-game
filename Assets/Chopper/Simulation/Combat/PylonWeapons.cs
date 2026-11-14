using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Chopper.Simulation.Combat
{
    /// <summary>How a pylon weapon behaves. One system per behaviour, not per weapon: new weapons are mostly data.</summary>
    public enum PylonBehaviour : byte
    {
        /// <summary>Rockets, bombs and mines, not ghosts (<see cref="RocketSystem"/>): the path follows from the launch.</summary>
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
        /// <summary>Rounds before a reload; a salvo is one round. 0 = no limit. See <see cref="Magazine"/>.</summary>
        public byte MagazineSize;
        /// <summary>Seconds after the last shot until the magazine is full again.</summary>
        public float ReloadSeconds;

        // Rocket only.
        /// <summary>Downward acceleration, m/s2 (bombs, heavy rockets). 0 = straight.</summary>
        public float Gravity;
        /// <summary>Speed lost per second as a rate (air mines stop and hang). 0 = none.</summary>
        public float Drag;

        // Guided: what the seeker locks. Rocket: what sets off the proximity fuse.
        public TargetClass Targets;

        // Guided only.
        public SeekerType Seeker;
        public float LockConeCos;
        public float LockRange;
        public float LockTime;
        /// <summary>Seconds the lock holds after the target leaves the cone or line of sight.</summary>
        public float LockGrace;
        /// <summary>True: cannot fire without a lock (no dumb-fire).</summary>
        public bool RequiresLock;
        /// <summary>Radians per second.</summary>
        public float TurnRate;
        public float NavigationGain;
        /// <summary>Guided and rocket: explodes this close to a target, metres. 0 = contact only.</summary>
        public float ProximityFuse;
        /// <summary>Seconds after launch before the missile steers (it clears the launcher first).</summary>
        public float ArmTime;
        public Entity MissilePrefab;
        /// <summary>Guided only: splits into submunitions (<see cref="ClusterConfig.Count"/> 0 = no).</summary>
        public ClusterConfig Cluster;
        /// <summary>Guided and rocket: shots per trigger pull (swarm, salvo pod). 0 or 1 = one shot.</summary>
        public byte SalvoCount;
        /// <summary>Seconds between shots of one salvo (one per tick at most, so predicted spawns match).</summary>
        public float SalvoInterval;
        /// <summary>Cosine of the launch spread cone of a salvo.</summary>
        public float SalvoConeCos;
        /// <summary>Guided only. 0..1: part of a salvo that flares do not fool (swarms saturate flares).</summary>
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
        /// <summary>Rounds used from the magazine, so the default is full (<see cref="Magazine"/>).</summary>
        [GhostField] public byte RoundsUsed;
        [GhostField] public float ReloadRemaining;
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
