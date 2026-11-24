using Chopper.Presentation.Bridge;
using Chopper.Simulation.Combat;
using Chopper.Simulation.Match;
using Chopper.Simulation.Session;
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
        private EntityQuery _respawnTimes;
        private EntityQuery _xpAwards;
        private EntityQuery _matchStates;
        private EntityQuery _teamScores;
        private EntityQuery _matchStats;
        private EntityQuery _remoteRockets;
        private EntityQuery _localRockets;
        private EntityQuery _missileDetonations;
        private EntityQuery _clusterSplits;
        private EntityQuery _chopperHits;
        private EntityQuery _wreckExplosions;
        private EntityQuery _disconnects;
        private int _localNetworkId;
        private NukeState _nuke;
        private bool _hasNuke;

        protected override void OnCreate()
        {
            base.OnCreate();
            _hitConfirms = Rpc<HitConfirmRpc>();
            _damageTaken = Rpc<DamageTakenRpc>();
            _scores = Rpc<ScoreRpc>();
            _playersLeft = Rpc<PlayerLeftRpc>();
            _kills = Rpc<KillFeedRpc>();
            _respawnTimes = Rpc<RespawnTimeRpc>();
            _xpAwards = Rpc<XpAwardRpc>();
            _matchStates = Rpc<MatchStateRpc>();
            _teamScores = Rpc<TeamScoreRpc>();
            _matchStats = Rpc<MatchStatsRpc>();
            _remoteRockets = Rpc<RocketLaunchRpc>();
            _localRockets = GetEntityQuery(ComponentType.ReadOnly<LocalRocketLaunch>());
            _missileDetonations = Rpc<MissileDetonatedRpc>();
            _clusterSplits = Rpc<ClusterSplitRpc>();
            _chopperHits = Rpc<ChopperHitRpc>();
            _wreckExplosions = Rpc<WreckExplodedRpc>();
            _disconnects = GetEntityQuery(ComponentType.ReadOnly<ClientDisconnect>());
        }

        protected override void OnUpdate()
        {
            if (TryGetContext(out var context))
            {
                Publish(context.Events);
                // Kept until a scene's presentation hears it: a link lost during a scene load reaches the next scene.
                foreach (var disconnect in SystemAPI.Query<RefRO<ClientDisconnect>>())
                    context.Events.Publish(new ConnectionLost(disconnect.ValueRO.Reason, disconnect.ValueRO.WasConnected != 0));
                EntityManager.DestroyEntity(_disconnects);
            }

            EntityManager.DestroyEntity(_hitConfirms);
            EntityManager.DestroyEntity(_damageTaken);
            EntityManager.DestroyEntity(_scores);
            EntityManager.DestroyEntity(_playersLeft);
            EntityManager.DestroyEntity(_kills);
            EntityManager.DestroyEntity(_respawnTimes);
            EntityManager.DestroyEntity(_xpAwards);
            EntityManager.DestroyEntity(_matchStates);
            EntityManager.DestroyEntity(_teamScores);
            EntityManager.DestroyEntity(_matchStats);
            EntityManager.DestroyEntity(_remoteRockets);
            EntityManager.DestroyEntity(_localRockets);
            EntityManager.DestroyEntity(_missileDetonations);
            EntityManager.DestroyEntity(_clusterSplits);
            EntityManager.DestroyEntity(_chopperHits);
            EntityManager.DestroyEntity(_wreckExplosions);
        }

        /// <summary>Capture the Nuke: compares the replicated nuke with last frame's and publishes what changed.</summary>
        private void PublishNuke(PresentationEvents events)
        {
            var seen = false;
            foreach (var nuke in SystemAPI.Query<RefRO<NukeState>>())
            {
                var now = nuke.ValueRO;
                if (_hasNuke)
                {
                    var change = Hud.NukeCallout.Diff(_nuke, now, out var team);
                    if (change != Hud.NukeChange.None) events.Publish(new NukeChanged(change, team));
                }
                _nuke = now;
                seen = true;
                break;
            }
            _hasNuke = seen;
        }

        private void Publish(PresentationEvents events)
        {
            PublishNuke(events);
            var localId = SystemAPI.TryGetSingleton<NetworkId>(out var id) ? id.Value : 0;
            if (localId != _localNetworkId)
            {
                _localNetworkId = localId;
                events.Publish(new LocalPlayerIdentified(localId));
            }

            // Match state first, so scores and kills land in the right match.
            foreach (var rpc in SystemAPI.Query<RefRO<MatchStateRpc>>().WithAll<ReceiveRpcCommandRequest>())
                events.Publish(new MatchStateChanged((MatchPhase)rpc.ValueRO.Phase, rpc.ValueRO.TimeRemaining, rpc.ValueRO.WinnerNetworkId,
                    (MatchMode)rpc.ValueRO.Mode, rpc.ValueRO.TeamCount, rpc.ValueRO.WinnerTeam));
            foreach (var rpc in SystemAPI.Query<RefRO<TeamScoreRpc>>().WithAll<ReceiveRpcCommandRequest>())
                events.Publish(new TeamScoreChanged(rpc.ValueRO.Team, rpc.ValueRO.Score));
            foreach (var rpc in SystemAPI.Query<RefRO<MatchStatsRpc>>().WithAll<ReceiveRpcCommandRequest>())
                events.Publish(new MatchStatsReceived(rpc.ValueRO.NetworkId, rpc.ValueRO.ToStats()));
            foreach (var rpc in SystemAPI.Query<RefRO<ScoreRpc>>().WithAll<ReceiveRpcCommandRequest>())
                events.Publish(new ScoreChanged(rpc.ValueRO.NetworkId, rpc.ValueRO.Kills, rpc.ValueRO.Deaths, rpc.ValueRO.Team));
            foreach (var rpc in SystemAPI.Query<RefRO<PlayerLeftRpc>>().WithAll<ReceiveRpcCommandRequest>())
                events.Publish(new PlayerLeft(rpc.ValueRO.NetworkId));
            foreach (var rpc in SystemAPI.Query<RefRO<KillFeedRpc>>().WithAll<ReceiveRpcCommandRequest>())
                events.Publish(new PlayerKilled(rpc.ValueRO.KillerNetworkId, rpc.ValueRO.VictimNetworkId));
            foreach (var rpc in SystemAPI.Query<RefRO<RespawnTimeRpc>>().WithAll<ReceiveRpcCommandRequest>())
                events.Publish(new RespawnCountdown(rpc.ValueRO.Seconds));
            foreach (var rpc in SystemAPI.Query<RefRO<XpAwardRpc>>().WithAll<ReceiveRpcCommandRequest>())
                events.Publish(new XpAwarded((XpReason)rpc.ValueRO.Reason, rpc.ValueRO.Amount, rpc.ValueRO.Other, rpc.ValueRO.DamageXp));

            foreach (var rpc in SystemAPI.Query<RefRO<HitConfirmRpc>>().WithAll<ReceiveRpcCommandRequest>())
                events.Publish(new HitConfirmed(rpc.ValueRO.Damage, rpc.ValueRO.Killed));
            foreach (var rpc in SystemAPI.Query<RefRO<DamageTakenRpc>>().WithAll<ReceiveRpcCommandRequest>())
            {
                var taken = rpc.ValueRO;
                events.Publish(new DamageTaken(taken.Amount, taken.HasSource, taken.SourcePosition, (DamageKind)taken.Kind, taken.ImpactSpeed, taken.Jam));
            }

            foreach (var rpc in SystemAPI.Query<RefRO<MissileDetonatedRpc>>().WithAll<ReceiveRpcCommandRequest>())
                events.Publish(new MissileDetonated(rpc.ValueRO.Position));
            foreach (var rpc in SystemAPI.Query<RefRO<WreckExplodedRpc>>().WithAll<ReceiveRpcCommandRequest>())
                events.Publish(new WreckExploded(rpc.ValueRO.Position, rpc.ValueRO.Velocity, rpc.ValueRO.Style, rpc.ValueRO.OwnerNetworkId,
                    rpc.ValueRO.PartSeed, rpc.ValueRO.Parts));
            foreach (var rpc in SystemAPI.Query<RefRO<ChopperHitRpc>>().WithAll<ReceiveRpcCommandRequest>())
            {
                var hit = rpc.ValueRO;
                events.Publish(new ChopperHit(hit.GhostId, hit.LocalPoint, hit.LocalNormal, hit.Amount, (DamageKind)hit.Kind));
            }

            foreach (var launch in SystemAPI.Query<RefRO<LocalRocketLaunch>>())
            {
                var own = launch.ValueRO;
                events.Publish(new RocketLaunched(own.Position, own.Velocity, own.Lifetime, localId, own.Gravity, own.Drag,
                    own.ProximityFuse, own.FuseTargets, own.SplashRadius));
            }
            foreach (var rpc in SystemAPI.Query<RefRO<RocketLaunchRpc>>().WithAll<ReceiveRpcCommandRequest>())
            {
                // Our own rockets were already published from prediction.
                if (rpc.ValueRO.ShooterNetworkId == localId) continue;
                var other = rpc.ValueRO;
                events.Publish(new RocketLaunched(other.Position, other.Velocity, other.Lifetime, other.ShooterNetworkId, other.Gravity,
                    other.Drag, other.ProximityFuse, (TargetClass)other.FuseTargets, other.SplashRadius));
            }

            // Cluster submunitions: rebuilt from the server's seed, drawn like rockets (also for the shooter: not predicted).
            foreach (var rpc in SystemAPI.Query<RefRO<ClusterSplitRpc>>().WithAll<ReceiveRpcCommandRequest>())
            {
                var split = rpc.ValueRO;
                for (var i = 0; i < split.Count; i++)
                {
                    var velocity = Cluster.Direction(split.Forward, split.ConeCos, split.Seed, i) * split.Speed;
                    events.Publish(new RocketLaunched(split.Position, velocity, split.Lifetime, split.ShooterNetworkId,
                        splashRadius: split.SplashRadius));
                }
            }
        }

        private EntityQuery Rpc<T>() where T : unmanaged, IRpcCommand =>
            GetEntityQuery(ComponentType.ReadOnly<T>(), ComponentType.ReadOnly<ReceiveRpcCommandRequest>());
    }
}
