using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;

namespace Chopper.Simulation.Session
{
    /// <summary>
    /// Client event entity: this client's link to the server closed (lost, timed out, refused, or the server stopped).
    /// Made by <see cref="ConnectionWatchSystem"/>. Presentation reads it once (and only then destroys it, so a
    /// disconnect during a scene load waits for the next scene) and goes back to a menu with a notice.
    /// </summary>
    public struct ClientDisconnect : IComponentData
    {
        public NetworkStreamDisconnectReason Reason;
        /// <summary>1 when the link was up (handshake done) before it closed; 0 when the connect itself failed.</summary>
        public byte WasConnected;
    }

    /// <summary>
    /// Client: turns Netcode's connection events (<see cref="NetworkStreamDriver.ConnectionEventsForTick"/>, valid one
    /// tick only) into a <see cref="ClientDisconnect"/> entity. Runs after the receive group, so it sees this tick's events.
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(NetworkReceiveSystemGroup))]
    public partial struct ConnectionWatchSystem : ISystem
    {
        private byte _connected;

        [BurstCompile]
        public void OnCreate(ref SystemState state) => state.RequireForUpdate<NetworkStreamDriver>();

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var events = SystemAPI.GetSingleton<NetworkStreamDriver>().ConnectionEventsForTick;
            for (var i = 0; i < events.Length; i++)
            {
                var connectionEvent = events[i];
                if (connectionEvent.State == ConnectionState.State.Connected) _connected = 1;
                if (connectionEvent.State != ConnectionState.State.Disconnected) continue;

                var entity = state.EntityManager.CreateEntity();
                state.EntityManager.AddComponentData(entity, new ClientDisconnect
                {
                    Reason = connectionEvent.DisconnectReason,
                    WasConnected = _connected,
                });
                _connected = 0;
            }
        }
    }
}
