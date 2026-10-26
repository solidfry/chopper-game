using Unity.Entities;

namespace Chopper.Simulation.Session
{
    /// <summary>Server only: the connection entity that owns this chopper. Used to send RPCs to its player.</summary>
    public struct ConnectionOwner : IComponentData
    {
        public Entity Value;
    }

    /// <summary>Server only, on a connection entity: seconds until that player gets a new chopper.</summary>
    public struct RespawnTimer : IComponentData
    {
        public float Remaining;
    }
}
