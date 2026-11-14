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
        /// <summary>The gun module's short name, for the match log (server only).</summary>
        public Unity.Collections.FixedString32Bytes Name;
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
