using Chopper.Simulation.Match;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace Chopper.Presentation.Hud
{
    /// <summary>Turns the server's match RPCs (score, kill feed, match state) into the scene's <see cref="MatchHudModel"/>.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class MatchHudSystem : SystemBase
    {
        private ChopperHudModelHost _host;
        private EntityQuery _scores;
        private EntityQuery _playersLeft;
        private EntityQuery _kills;
        private EntityQuery _matchStates;

        protected override void OnCreate()
        {
            RequireForUpdate<NetworkStreamInGame>();
            _scores = SystemAPI.QueryBuilder().WithAll<ScoreRpc, ReceiveRpcCommandRequest>().Build();
            _playersLeft = SystemAPI.QueryBuilder().WithAll<PlayerLeftRpc, ReceiveRpcCommandRequest>().Build();
            _kills = SystemAPI.QueryBuilder().WithAll<KillFeedRpc, ReceiveRpcCommandRequest>().Build();
            _matchStates = SystemAPI.QueryBuilder().WithAll<MatchStateRpc, ReceiveRpcCommandRequest>().Build();
        }

        protected override void OnUpdate()
        {
            if (_host == null)
                _host = Object.FindFirstObjectByType<ChopperHudModelHost>();

            var model = _host != null ? _host.Match : null;
            if (model != null)
            {
                if (SystemAPI.TryGetSingleton<NetworkId>(out var localId))
                    model.SetLocalPlayer(localId.Value);

                foreach (var rpc in SystemAPI.Query<RefRO<MatchStateRpc>>().WithAll<ReceiveRpcCommandRequest>())
                    model.SetMatch((MatchPhase)rpc.ValueRO.Phase, rpc.ValueRO.TimeRemaining, rpc.ValueRO.WinnerNetworkId);
                foreach (var rpc in SystemAPI.Query<RefRO<ScoreRpc>>().WithAll<ReceiveRpcCommandRequest>())
                    model.ApplyScore(rpc.ValueRO.NetworkId, rpc.ValueRO.Kills, rpc.ValueRO.Deaths);
                foreach (var rpc in SystemAPI.Query<RefRO<PlayerLeftRpc>>().WithAll<ReceiveRpcCommandRequest>())
                    model.RemovePlayer(rpc.ValueRO.NetworkId);
                foreach (var rpc in SystemAPI.Query<RefRO<KillFeedRpc>>().WithAll<ReceiveRpcCommandRequest>())
                    model.AddKill(rpc.ValueRO.KillerNetworkId, rpc.ValueRO.VictimNetworkId);

                model.Tick(UnityEngine.Time.deltaTime);
            }

            // Always consume, also without a HUD, so RPC entities do not pile up.
            EntityManager.DestroyEntity(_scores);
            EntityManager.DestroyEntity(_playersLeft);
            EntityManager.DestroyEntity(_kills);
            EntityManager.DestroyEntity(_matchStates);
        }
    }
}
