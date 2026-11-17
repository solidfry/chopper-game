using Chopper.Simulation.Session;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace Chopper.Simulation.Match
{
    /// <summary>
    /// Server only. Owns the gameplay event buffer (<see cref="MatchLogEntry"/>): makes it, counts each player's match
    /// stats from it (<see cref="MatchStatsRules"/>) and clears it each frame, after <see cref="MatchLogSystem"/> has
    /// written it. When the match ends it sends every player's stats to every client (<see cref="MatchStatsRpc"/>);
    /// when the next match starts it sets them back to zero.
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(MatchLogSystem))]
    public partial struct MatchStatsSystem : ISystem
    {
        private MatchPhase _phase;
        private bool _firstBloodTaken;
        /// <summary>Damage per attacker on each victim's current life, for assists and the kill's damage line.</summary>
        private NativeList<DamageShare> _shares;

        public void OnCreate(ref SystemState state)
        {
            var entity = state.EntityManager.CreateEntity();
            state.EntityManager.SetName(entity, "MatchLog");
            state.EntityManager.AddBuffer<MatchLogEntry>(entity);
            _shares = new NativeList<DamageShare>(64, Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState state)
        {
            if (_shares.IsCreated) _shares.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (_, connection) in SystemAPI.Query<RefRO<PlayerScore>>().WithNone<PlayerStats>().WithEntityAccess())
                ecb.AddComponent<PlayerStats>(connection);
            ecb.Playback(state.EntityManager);
            ecb.Dispose();

            var phase = SystemAPI.TryGetSingleton<MatchState>(out var match) ? match.Phase : MatchPhase.InProgress;
            var buffer = SystemAPI.GetSingletonBuffer<MatchLogEntry>();

            if (phase == MatchPhase.InProgress && _phase != MatchPhase.InProgress)
                Reset(ref state);

            // Counted while the match runs; the end frame's events are counted before the stats are sent.
            if (phase == MatchPhase.InProgress || _phase == MatchPhase.InProgress)
                Count(ref state, buffer);
            buffer.Clear();

            if (phase != MatchPhase.InProgress && _phase == MatchPhase.InProgress)
                SendAll(ref state);
            _phase = phase;
        }

        private void Count(ref SystemState state, DynamicBuffer<MatchLogEntry> buffer)
        {
            if (buffer.Length == 0) return;
            var connections = new NativeHashMap<int, Entity>(16, Allocator.Temp);
            foreach (var (networkId, connection) in SystemAPI.Query<RefRO<PlayerId>>().WithAll<PlayerStats>().WithEntityAccess())
                connections[networkId.ValueRO.Value] = connection;

            var statsLookup = SystemAPI.GetComponentLookup<PlayerStats>();
            var networkIds = SystemAPI.GetComponentLookup<NetworkId>(true);
            var rates = SystemAPI.TryGetSingleton<Progression.ProgressionRules>(out var rules) ? rules.Xp : Progression.XpRates.Default;
            var awards = new NativeList<XpAward>(4, Allocator.Temp);
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var entry in buffer)
            {
                // XP popups (kills, assists, repairs, evaded missiles), also counted into the stats for the end screen.
                awards.Clear();
                XpAwards.Collect(entry, _shares, rates, awards);
                foreach (var award in awards)
                {
                    if (!connections.TryGetValue(award.Player, out var recipient)) continue;
                    var stats = statsLookup[recipient];
                    XpAwards.Count(award, ref stats.Value);
                    statsLookup[recipient] = stats;
                    if (!networkIds.HasComponent(recipient)) continue;
                    var rpc = ecb.CreateEntity();
                    ecb.AddComponent(rpc, new XpAwardRpc { Reason = (byte)award.Reason, Amount = award.Amount, Other = award.Other, DamageXp = award.DamageXp });
                    ecb.AddComponent(rpc, new SendRpcCommandRequest { TargetConnection = recipient });
                }

                var hasActor = connections.TryGetValue(entry.Actor, out var actorEntity);
                var hasTarget = connections.TryGetValue(entry.Target, out var targetEntity);
                if (!hasActor && !hasTarget) continue;

                var actor = hasActor ? statsLookup[actorEntity] : default;
                // Same player on both sides: one copy, so neither write is lost.
                var same = hasActor && hasTarget && actorEntity == targetEntity;
                if (same)
                {
                    MatchStatsRules.Apply(entry, ref actor, true, ref actor, true, ref _firstBloodTaken);
                    statsLookup[actorEntity] = actor;
                    continue;
                }
                var target = hasTarget ? statsLookup[targetEntity] : default;
                MatchStatsRules.Apply(entry, ref actor, hasActor, ref target, hasTarget, ref _firstBloodTaken);
                if (hasActor) statsLookup[actorEntity] = actor;
                if (hasTarget) statsLookup[targetEntity] = target;
            }
            connections.Dispose();
            awards.Dispose();
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }

        private void Reset(ref SystemState state)
        {
            _firstBloodTaken = false;
            _shares.Clear();
            foreach (var stats in SystemAPI.Query<RefRW<PlayerStats>>())
                stats.ValueRW = default;
        }

        private void SendAll(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (stats, networkId) in SystemAPI.Query<RefRO<PlayerStats>, RefRO<PlayerId>>())
            {
                var rpc = ecb.CreateEntity();
                ecb.AddComponent(rpc, MatchStatsRpc.From(networkId.ValueRO.Value, stats.ValueRO.Value));
                // No target connection: every client gets every player's stats (awards compare players).
                ecb.AddComponent<SendRpcCommandRequest>(rpc);
            }
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
}
