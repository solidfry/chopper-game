using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Chopper.Simulation.Combat
{
    /// <summary>
    /// Fast nose gun (machine gun, autocannon). Fires along the chopper's forward axis. Baked from
    /// <see cref="Flight.ChopperAuthoring"/>; later this comes from the nose weapon module.
    /// </summary>
    public struct HitscanWeaponConfig : IComponentData
    {
        /// <summary>Seconds between shots.</summary>
        public float FireInterval;
        public float Damage;
        public float Range;
        /// <summary>Damage starts to fall at this distance, metres. 0 = full damage to <see cref="Range"/>.</summary>
        public float FalloffStart;
        /// <summary>Part of the damage left at <see cref="Range"/> (0..1). Used only with <see cref="FalloffStart"/>.</summary>
        public float FalloffFloor;
        /// <summary>Gun position in chopper space.</summary>
        public float3 MuzzleOffset;
        /// <summary>Heat each shot adds (1 = full). 0 = no heat limit. See <see cref="GunHeat"/>.</summary>
        public float HeatPerShot;
        /// <summary>Heat lost per second when cooling.</summary>
        public float CoolRate;
        /// <summary>
        /// Round speed, m/s, on top of the chopper's velocity. 0 = instant (one ray, hitscan). Above 0 the server flies
        /// each round (<see cref="ActiveBullet"/>, <see cref="BulletStepSystem"/>) and players must lead the target.
        /// </summary>
        public float BulletSpeed;
        /// <summary>Spin gained per second while the trigger is held (<see cref="GunSpin"/>). 0 = no spin-up.</summary>
        public float SpinUpRate;
        /// <summary>Spin lost per second after the trigger is let go.</summary>
        public float SpinDownRate;
        /// <summary>The gun module's short name, for the match log (server only).</summary>
        public Unity.Collections.FixedString32Bytes Name;
        /// <summary>The gun module's id (<see cref="Loadout.ModuleDefinition.Id"/>), for the client's look and sound.
        /// Local on both sides (the loadout is applied on each), not sent. 0 = none (turrets: gun kind default).</summary>
        public ushort ModuleId;
    }

    /// <summary>
    /// Server only: one nose-gun round in flight, in the one buffer that <see cref="BulletStepSystem"/> makes. Not a ghost:
    /// clients draw tracers from <see cref="HitscanWeaponState.ShotCount"/>, so a fast gun costs no extra bandwidth.
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct ActiveBullet : IBufferElementData
    {
        public float3 Position;
        /// <summary>World velocity: the chopper's velocity at fire time plus nose × <see cref="HitscanWeaponConfig.BulletSpeed"/>.</summary>
        public float3 Velocity;
        /// <summary>Seconds of flight left (range / bullet speed at fire time).</summary>
        public float TimeLeft;
        /// <summary>Metres flown so far (damage falloff and the match log).</summary>
        public float Travelled;
        public Entity Shooter;
        /// <summary>
        /// Lag compensation: ticks to rewind the collision world, the shooter's interpolation delay at fire time (capped).
        /// Each step rewinds by this much from the current tick, so the lead the shooter saw stays right.
        /// </summary>
        public uint RewindTicks;
        /// <summary>The gun at fire time: damage, falloff and name.</summary>
        public HitscanWeaponConfig Gun;
        /// <summary>The shooter at fire time, for kill credit if its chopper is gone when the round lands.</summary>
        public AttackerSnapshot Attacker;
    }

    /// <summary>
    /// Predicted on the owner so the fire rhythm matches the server; interpolated for other players. Fields that gate
    /// firing are not quantized (see <see cref="PylonWeaponState"/>).
    /// </summary>
    public struct HitscanWeaponState : IComponentData
    {
        [GhostField] public float CooldownRemaining;
        /// <summary>Shots fired so far. Sent to everyone; clients draw a tracer each time it goes up.</summary>
        [GhostField] public uint ShotCount;
        /// <summary>0 = cold, 1 = overheated.</summary>
        [GhostField] public float Heat;
        /// <summary>Seconds before the gun starts to cool.</summary>
        [GhostField] public float CoolDelay;
        /// <summary>Reached full heat: no fire until it cools to <see cref="GunHeat.ResumeHeat"/>.</summary>
        [GhostField] public bool Overheated;
        /// <summary>Barrel spin, 0 (still) to 1 (firing speed). Guns with no spin-up stay at 1. Sent to everyone for the spin sounds.</summary>
        [GhostField(Quantization = 0)] public float Spin;
    }

    /// <summary>Gun damage maths shared by the fire system, the hangar and tests. Pure, Burst friendly.</summary>
    public static class GunDamage
    {
        /// <summary>Damage of one shot that hits at <paramref name="distance"/>: full to the falloff start, then a
        /// straight line down to the floor at full range.</summary>
        public static float At(in HitscanWeaponConfig config, float distance)
        {
            if (config.FalloffStart <= 0f || config.FalloffStart >= config.Range) return config.Damage;
            var t = math.saturate((distance - config.FalloffStart) / (config.Range - config.FalloffStart));
            return config.Damage * math.lerp(1f, math.saturate(config.FalloffFloor), t);
        }
    }
}
