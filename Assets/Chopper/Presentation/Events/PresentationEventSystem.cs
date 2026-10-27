using Chopper.Presentation.Bridge;
using Chopper.Simulation.Combat;
using Chopper.Simulation.Match;
using Unity.Entities;
using Unity.NetCode;

namespace Chopper.Presentation.Events
{
    /// <summary>
    /// Client only. The single place that reads simulation events (server RPCs, predicted event entities) and
    /// publishes them as presentation messages. Always destroys the entities, also with no presentation in the scene,
    /// so they do not pile up. Feature code never queries these entities itself.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup), OrderFirst = true)]
    [UpdateAfter(typeof(PresentationBridgeSystem))]
    public partial class PresentationEventSystem : PresentationSystemBase
    {
        private EntityQuery _hitConfirms;
        private EntityQuery _damageTaken;
        private EntityQuery _scores;
        private EntityQuery _playersLeft;
        private EntityQuery _kills;
        private EntityQuery _matchStates;
        private EntityQuery _remoteRockets;
        private EntityQuery _localRockets;
        private EntityQuery _missileDetonations;
        private int _localNetworkId;

        protected override void OnCreate()
        {
            base.OnCreate();
            _hitConfirms = Rpc<HitConfirmRpc>();
            _damageTaken = Rpc<DamageTakenRpc>();
            _scores = Rpc<ScoreRpc>();
            _playersLeft = Rpc<PlayerLeftRpc>();
            _kills = Rpc<KillFeedRpc>();
            _matchStates = Rpc<MatchStateRpc>();
            _remoteRockets = Rpc<RocketLaunchRpc>();
            _localRockets = GetEntityQuery(ComponentType.ReadOnly<LocalRocketLaunch>());
            _missileDetonations = Rpc<MissileDetonatedRpc>();
        }

        protected override void OnUpdate()
        {
            if (TryGetContext(out var context))
                Publish(context.Events);

            EntityManager.DestroyEntity(_hitConfirms);
            EntityManager.DestroyEntity(_damageTaken);
            EntityManager.DestroyEntity(_scores);
            EntityManager.DestroyEntity(_playersLeft);
            EntityManager.DestroyEntity(_kills);
            EntityManager.DestroyEntity(_matchStates);
            EntityManager.DestroyEntity(_remoteRockets);
            EntityManager.DestroyEntity(_localRockets);
            EntityManager.DestroyEntity(_missileDetonations);
        }

        private void Publish(PresentationEvents events)
        {
            var localId = SystemAPI.TryGetSingleton<NetworkId>(out var id) ? id.Value : 0;
            if (localId != _localNetworkId)
            {
                _localNetworkId = localId;
                events.Publish(new LocalPlayerIdentified(localId));
            }

            // Match state first, so scores and kills land in the right match.
            foreach (var rpc in SystemAPI.Query<RefRO<MatchStateRpc>>().WithAll<ReceiveRpcCommandRequest>())
                events.Publish(new MatchStateChanged((MatchPhase)rpc.ValueRO.Phase, rpc.ValueRO.TimeRemaining, rpc.ValueRO.WinnerNetworkId));
            foreach (var rpc in SystemAPI.Query<RefRO<ScoreRpc>>().WithAll<ReceiveRpcCommandRequest>())
                events.Publish(new ScoreChanged(rpc.ValueRO.NetworkId, rpc.ValueRO.Kills, rpc.ValueRO.Deaths));
            foreach (var rpc in SystemAPI.Query<RefRO<PlayerLeftRpc>>().WithAll<ReceiveRpcCommandRequest>())
                events.Publish(new PlayerLeft(rpc.ValueRO.NetworkId));
            foreach (var rpc in SystemAPI.Query<RefRO<KillFeedRpc>>().WithAll<ReceiveRpcCommandRequest>())
                events.Publish(new PlayerKilled(rpc.ValueRO.KillerNetworkId, rpc.ValueRO.VictimNetworkId));

            foreach (var rpc in SystemAPI.Query<RefRO<HitConfirmRpc>>().WithAll<ReceiveRpcCommandRequest>())
                events.Publish(new HitConfirmed(rpc.ValueRO.Damage, rpc.ValueRO.Killed));
            foreach (var rpc in SystemAPI.Query<RefRO<DamageTakenRpc>>().WithAll<ReceiveRpcCommandRequest>())
                events.Publish(new DamageTaken(rpc.ValueRO.Amount, rpc.ValueRO.HasSource, rpc.ValueRO.SourcePosition));

            foreach (var rpc in SystemAPI.Query<RefRO<MissileDetonatedRpc>>().WithAll<ReceiveRpcCommandRequest>())
                events.Publish(new MissileDetonated(rpc.ValueRO.Position));

            foreach (var launch in SystemAPI.Query<RefRO<LocalRocketLaunch>>())
                events.Publish(new RocketLaunched(launch.ValueRO.Position, launch.ValueRO.Velocity, launch.ValueRO.Lifetime, localId));
            foreach (var rpc in SystemAPI.Query<RefRO<RocketLaunchRpc>>().WithAll<ReceiveRpcCommandRequest>())
            {
                // Our own rockets were already published from prediction.
                if (rpc.ValueRO.ShooterNetworkId == localId) continue;
                events.Publish(new RocketLaunched(rpc.ValueRO.Position, rpc.ValueRO.Velocity, rpc.ValueRO.Lifetime, rpc.ValueRO.ShooterNetworkId));
            }
        }

        private EntityQuery Rpc<T>() where T : unmanaged, IRpcCommand =>
            GetEntityQuery(ComponentType.ReadOnly<T>(), ComponentType.ReadOnly<ReceiveRpcCommandRequest>());
    }
}
