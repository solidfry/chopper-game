#if UNITY_EDITOR || DEBUG
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace Chopper.Simulation.Session
{
    /// <summary>
    /// Dev only. If a world has no chopper a few seconds after its first connection goes in game, logs once what is
    /// missing on the way to a spawn (in-game state, spawner from the subscene, chopper ghost), so a failed spawn says
    /// why. The clock starts at in game, not at world start: a matchmade client's world waits for the Edgegap server.
    /// Server: widens Netcode's "Server Tick Batching" warning window (see <see cref="BatchWarningWindowFrames"/>) and
    /// mutes it while the match loads (<see cref="LoadGraceSeconds"/>). The global Netcode config (Setup step 1) lets
    /// the server run up to 4 full ticks in a slow frame, so batching itself is now only for a real overload.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation)]
    public partial class SessionDiagnosticsSystem : SystemBase
    {
        private const double CheckAfterSeconds = 5.0;
        /// <summary>
        /// Netcode averages batched ticks over 4 frames, so one editor hitch (an editor GC of about 20 ms) is a
        /// warning. Measured 29 Oct: 0.2 % of frames batched, each alone. Measured 6 Nov (Map_City04, RTX 5090): server sim 1.2 ms, frames 10 ms, none over 18 ms in the worst views; with a 60 frame window one 200 ms stall (editor compile, focus change) still warned. Over this many frames (about 1-4 s in the
        /// editor) the warning means the server really falls behind. A window set in Play Mode Tools wins.
        /// </summary>
        private const int BatchWarningWindowFrames = 240;
        private const int NetcodeDefaultWindowFrames = 4;
        /// <summary>Loading the subscene stalls a frame or two and the server catches up once; that is not a problem.</summary>
        private const double LoadGraceSeconds = 15.0;

        private bool _done;
        private double _inGameSince = -1.0;
        private bool _batchWindowSet;
        private bool _warnAfterLoad;
        private bool _loadGraceOver;

        protected override void OnUpdate()
        {
            if (World.IsServer() && !_loadGraceOver && SystemAPI.TryGetSingletonRW<NetDebug>(out var netDebug))
            {
                if (!_batchWindowSet)
                {
                    _batchWindowSet = true;
                    if (netDebug.ValueRO.WarnBatchedTicksRollingWindowSize == NetcodeDefaultWindowFrames)
                        netDebug.ValueRW.WarnBatchedTicksRollingWindowSize = BatchWarningWindowFrames;
                    _warnAfterLoad = netDebug.ValueRO.WarnBatchedTicks;
                    netDebug.ValueRW.WarnBatchedTicks = false;
                }
                else if (SystemAPI.Time.ElapsedTime >= LoadGraceSeconds)
                {
                    _loadGraceOver = true;
                    netDebug.ValueRW.WarnBatchedTicks = _warnAfterLoad;
                }
            }

            if (_done) return;
            var inGame = SystemAPI.QueryBuilder().WithAll<NetworkId, NetworkStreamInGame>().Build().CalculateEntityCount();
            var now = SystemAPI.Time.ElapsedTime;
            if (_inGameSince < 0.0)
            {
                if (inGame > 0) _inGameSince = now;
                return;
            }
            if (now - _inGameSince < CheckAfterSeconds) return;
            _done = true;

            var choppers = SystemAPI.QueryBuilder().WithAll<GhostOwner, Flight.ChopperFlightConfig>().Build().CalculateEntityCount();
            if (choppers > 0) return;

            var connections = SystemAPI.QueryBuilder().WithAll<NetworkId>().Build().CalculateEntityCount();
            var spawners = SystemAPI.QueryBuilder().WithAll<ChopperSpawner>().Build().CalculateEntityCount();
            var prefabValid = SystemAPI.TryGetSingleton<ChopperSpawner>(out var spawner) &&
                              EntityManager.Exists(spawner.Prefab);

            Debug.LogWarning($"[{World.Name}] No chopper {CheckAfterSeconds:0} s after going in game. Connections: {connections}, " +
                             $"in game: {inGame}, ChopperSpawner (from subscene): {spawners}, spawner prefab valid: {prefabValid}. " +
                             "Spawner 0 = subscene not loaded or not baked (reimport it).");
        }
    }
}
#endif
