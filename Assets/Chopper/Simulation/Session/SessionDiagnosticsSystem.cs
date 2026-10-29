#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace Chopper.Simulation.Session
{
    /// <summary>
    /// Dev only. If a world has no chopper a few seconds after start, logs once what is missing on the way to a spawn
    /// (connection, in-game state, spawner from the subscene, chopper ghost), so a failed spawn says why.
    /// Server: widens Netcode's "Server Tick Batching" warning window (see <see cref="BatchWarningWindowFrames"/>).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation)]
    public partial class SessionDiagnosticsSystem : SystemBase
    {
        private const double CheckAfterSeconds = 5.0;
        /// <summary>
        /// Netcode averages batched ticks over 4 frames, so one editor hitch (an editor GC of about 20 ms) is a
        /// warning. Measured 29 Oct: 0.2 % of frames batched, each alone. Over this many frames (about 0.3-1 s in the
        /// editor) the warning means the server really falls behind. A window set in Play Mode Tools wins.
        /// </summary>
        private const int BatchWarningWindowFrames = 60;
        private const int NetcodeDefaultWindowFrames = 4;

        private bool _done;
        private bool _batchWindowSet;

        protected override void OnUpdate()
        {
            if (!_batchWindowSet && World.IsServer() && SystemAPI.TryGetSingletonRW<NetDebug>(out var netDebug))
            {
                _batchWindowSet = true;
                if (netDebug.ValueRO.WarnBatchedTicksRollingWindowSize == NetcodeDefaultWindowFrames)
                    netDebug.ValueRW.WarnBatchedTicksRollingWindowSize = BatchWarningWindowFrames;
            }

            if (_done || SystemAPI.Time.ElapsedTime < CheckAfterSeconds) return;
            _done = true;

            var choppers = SystemAPI.QueryBuilder().WithAll<GhostOwner, Flight.ChopperFlightConfig>().Build().CalculateEntityCount();
            if (choppers > 0) return;

            var connections = SystemAPI.QueryBuilder().WithAll<NetworkId>().Build().CalculateEntityCount();
            var inGame = SystemAPI.QueryBuilder().WithAll<NetworkId, NetworkStreamInGame>().Build().CalculateEntityCount();
            var spawners = SystemAPI.QueryBuilder().WithAll<ChopperSpawner>().Build().CalculateEntityCount();
            var prefabValid = SystemAPI.TryGetSingleton<ChopperSpawner>(out var spawner) &&
                              EntityManager.Exists(spawner.Prefab);

            Debug.LogWarning($"[{World.Name}] No chopper after {CheckAfterSeconds:0} s. Connections: {connections}, " +
                             $"in game: {inGame}, ChopperSpawner (from subscene): {spawners}, spawner prefab valid: {prefabValid}. " +
                             "Spawner 0 = subscene not loaded or not baked (reimport it). Connections 0 = client did not connect.");
        }
    }
}
#endif
