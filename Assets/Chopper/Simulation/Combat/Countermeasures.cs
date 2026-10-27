using Unity.Entities;
using Unity.NetCode;

namespace Chopper.Simulation.Combat
{
    /// <summary>Flares, from the utility module's countermeasure definition.</summary>
    public struct CountermeasureConfig : IComponentData
    {
        public float Cooldown;
        /// <summary>Seconds the chopper cannot be heat-locked after flares, and heat seekers chasing it lose it.</summary>
        public float DecoyDuration;
    }

    /// <summary>Predicted on the owner, sent to everyone (<see cref="FlareCount"/> drives the flare visuals).</summary>
    public struct CountermeasureState : IComponentData
    {
        [GhostField(Quantization = 100)] public float CooldownRemaining;
        [GhostField(Quantization = 100)] public float DecoyRemaining;
        /// <summary>Times flares were dropped. Clients show a flare burst each time it goes up.</summary>
        [GhostField] public uint FlareCount;
    }
}
