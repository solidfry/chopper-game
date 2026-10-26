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
        /// <summary>Gun position in chopper space.</summary>
        public float3 MuzzleOffset;
    }

    /// <summary>Predicted on the owner so the fire rhythm matches the server.</summary>
    public struct HitscanWeaponState : IComponentData
    {
        [GhostField(Quantization = 1000)] public float CooldownRemaining;
    }
}
