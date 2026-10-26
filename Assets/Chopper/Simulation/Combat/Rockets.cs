using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Chopper.Simulation.Combat
{
    /// <summary>
    /// Pylon rockets: unguided, straight flight, direct hit plus splash. Baked from <see cref="Flight.ChopperAuthoring"/>;
    /// later this comes from the pylon module.
    /// </summary>
    public struct RocketLauncherConfig : IComponentData
    {
        public float FireInterval;
        /// <summary>m/s, added to the chopper's own velocity.</summary>
        public float Speed;
        public float Lifetime;
        public float DirectDamage;
        /// <summary>Damage at the centre of the blast; falls to 0 at <see cref="SplashRadius"/>.</summary>
        public float SplashDamage;
        public float SplashRadius;
        /// <summary>Pylon position in chopper space.</summary>
        public float3 MuzzleOffset;
    }

    /// <summary>Predicted on the owner so the fire rhythm matches the server.</summary>
    public struct RocketLauncherState : IComponentData
    {
        [GhostField(Quantization = 1000)] public float CooldownRemaining;
    }

    /// <summary>
    /// Server only: a rocket in flight. Not a ghost: flight is a straight line, so clients draw it from the launch
    /// alone (<see cref="RocketLaunchRpc"/>). No target rewind: others can dodge what they see.
    /// </summary>
    public struct Rocket : IComponentData
    {
        public float3 Position;
        public float3 Velocity;
        public float Remaining;
        public Entity Shooter;
        public float DirectDamage;
        public float SplashDamage;
        public float SplashRadius;
    }

    /// <summary>Server to all clients: a rocket was launched. The shooter's client ignores its own (it predicted it).</summary>
    public struct RocketLaunchRpc : IRpcCommand
    {
        public float3 Position;
        public float3 Velocity;
        public float Lifetime;
        public float SplashRadius;
        public int ShooterNetworkId;
    }

    /// <summary>Client only, one entity per predicted launch, for presentation (draw the rocket at once).</summary>
    public struct LocalRocketLaunch : IComponentData
    {
        public float3 Position;
        public float3 Velocity;
        public float Lifetime;
        public float SplashRadius;
    }
}
