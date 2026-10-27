using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Chopper.Simulation.Combat
{
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
