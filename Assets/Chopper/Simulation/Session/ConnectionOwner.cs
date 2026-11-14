using Unity.Entities;

namespace Chopper.Simulation.Session
{
    /// <summary>
    /// Server only: the player entity that owns this chopper: a connection, or a server bot entity
    /// (<see cref="Bots.ServerBot"/>). Send an RPC to it only when it has a <c>NetworkId</c> (a bot has none).
    /// </summary>
    public struct ConnectionOwner : IComponentData
    {
        public Entity Value;
    }

    /// <summary>
    /// Server only, on each player entity: the id the match knows the player by (score, kill feed, spawn, logs; also
    /// the chopper's <c>GhostOwner.NetworkId</c>). A connection: its <c>NetworkId</c>, set when it goes in game
    /// (<see cref="GoInGameServerSystem"/>). A server bot: <see cref="Bots.BotIds"/> (1001 and up).
    /// </summary>
    public struct PlayerId : IComponentData
    {
        public int Value;
    }

    /// <summary>Server only, on a player entity: seconds until that player gets a new chopper.</summary>
    public struct RespawnTimer : IComponentData
    {
        public float Remaining;
    }
}
