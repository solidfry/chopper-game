using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace Chopper.Simulation.Match
{
    /// <summary>
    /// Server only. Free For All: keeps each player's kills and deaths, runs the match timer, ends the match at the
    /// score or time limit, then starts the next one. Tells clients through RPCs (score, kill feed, match state).
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct MatchScoreSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            var rules = MatchRules.Default;
            state.EntityManager.CreateSingleton(new MatchState
            {
                Phase = MatchPhase.InProgress,
                TimeRemaining = rules.TimeLimit,
            });
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var rules = SystemAPI.TryGetSingleton<MatchRules>(out var baked) ? baked : MatchRules.Default;
            var matchEntity = SystemAPI.GetSingletonEntity<MatchState>();
            var match = SystemAPI.GetComponent<MatchState>(matchEntity);
            var previousPhase = match.Phase;

            AddJoinedPlayers(ref state, match);
            RemoveLeftPlayers(ref state);

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            var connections = new NativeHashMap<int, Entity>(16, Allocator.Temp);
            foreach (var (networkId, connection) in SystemAPI.Query<RefRO<NetworkId>>().WithAll<PlayerScore>().WithEntityAccess())
                connections[networkId.ValueRO.Value] = connection;

            foreach (var (kill, killEntity) in SystemAPI.Query<RefRO<KillEvent>>().WithEntityAccess())
            {
                ecb.DestroyEntity(killEntity);
                if (match.Phase != MatchPhase.InProgress) continue;

                var killer = kill.ValueRO.KillerNetworkId;
                var victim = kill.ValueRO.VictimNetworkId;
                Broadcast(ecb, new KillFeedRpc { KillerNetworkId = killer, VictimNetworkId = victim });

                if (connections.TryGetValue(victim, out var victimConnection))
                    ChangeScore(ref state, ecb, victimConnection, victim, kills: 0, deaths: 1);

                if (killer == 0 || killer == victim || !connections.TryGetValue(killer, out var killerConnection))
                    continue;

                var killerScore = ChangeScore(ref state, ecb, killerConnection, killer, kills: 1, deaths: 0);
                if (killerScore.Kills >= rules.ScoreLimit)
                    EndMatch(ref match, rules, killer);
            }

            match.TimeRemaining -= SystemAPI.Time.DeltaTime;
            if (match.TimeRemaining <= 0f)
            {
                if (match.Phase == MatchPhase.InProgress)
                {
                    EndMatch(ref match, rules, TopScorer(ref state));
                }
                else
                {
                    match = new MatchState { Phase = MatchPhase.InProgress, TimeRemaining = rules.TimeLimit };
                    foreach (var (score, networkId) in SystemAPI.Query<RefRW<PlayerScore>, RefRO<NetworkId>>())
                    {
                        score.ValueRW = default;
                        Broadcast(ecb, new ScoreRpc { NetworkId = networkId.ValueRO.Value });
                    }
                }
            }

            if (match.Phase != previousPhase)
                Broadcast(ecb, ToRpc(match));

            SystemAPI.SetComponent(matchEntity, match);
            ecb.Playback(state.EntityManager);
        }

        /// <summary>New players get a score entry, the current scoreboard and the match state.</summary>
        private void AddJoinedPlayers(ref SystemState state, in MatchState match)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (networkId, connection) in SystemAPI.Query<RefRO<NetworkId>>()
                         .WithAll<NetworkStreamInGame>().WithNone<PlayerScore>().WithEntityAccess())
            {
                var id = networkId.ValueRO.Value;
                ecb.AddComponent<PlayerScore>(connection);
                ecb.AddComponent(connection, new PlayerScoreCleanup { NetworkId = id });
                Broadcast(ecb, new ScoreRpc { NetworkId = id });

                foreach (var (otherScore, otherId) in SystemAPI.Query<RefRO<PlayerScore>, RefRO<NetworkId>>())
                {
                    Send(ecb, connection, new ScoreRpc
                    {
                        NetworkId = otherId.ValueRO.Value,
                        Kills = otherScore.ValueRO.Kills,
                        Deaths = otherScore.ValueRO.Deaths,
                    });
                }
                Send(ecb, connection, ToRpc(match));
            }
            ecb.Playback(state.EntityManager);
        }

        private void RemoveLeftPlayers(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (cleanup, entity) in SystemAPI.Query<RefRO<PlayerScoreCleanup>>().WithNone<NetworkId>().WithEntityAccess())
            {
                Broadcast(ecb, new PlayerLeftRpc { NetworkId = cleanup.ValueRO.NetworkId });
                ecb.RemoveComponent<PlayerScoreCleanup>(entity);
            }
            ecb.Playback(state.EntityManager);
        }

        private PlayerScore ChangeScore(ref SystemState state, EntityCommandBuffer ecb, Entity connection, int networkId, int kills, int deaths)
        {
            var score = SystemAPI.GetComponent<PlayerScore>(connection);
            score.Kills += kills;
            score.Deaths += deaths;
            SystemAPI.SetComponent(connection, score);
            Broadcast(ecb, new ScoreRpc { NetworkId = networkId, Kills = score.Kills, Deaths = score.Deaths });
            return score;
        }

        /// <summary>Most kills wins; a tie at the top is a draw (0).</summary>
        private int TopScorer(ref SystemState state)
        {
            var best = 0;
            var bestKills = 0;
            var tie = false;
            foreach (var (score, networkId) in SystemAPI.Query<RefRO<PlayerScore>, RefRO<NetworkId>>())
            {
                var kills = score.ValueRO.Kills;
                if (kills > bestKills)
                {
                    best = networkId.ValueRO.Value;
                    bestKills = kills;
                    tie = false;
                }
                else if (kills == bestKills && kills > 0)
                {
                    tie = true;
                }
            }
            return tie ? 0 : best;
        }

        private static void EndMatch(ref MatchState match, in MatchRules rules, int winner)
        {
            match = new MatchState
            {
                Phase = MatchPhase.PostGame,
                TimeRemaining = rules.PostGameDuration,
                WinnerNetworkId = winner,
            };
        }

        private static MatchStateRpc ToRpc(in MatchState match) => new()
        {
            Phase = (byte)match.Phase,
            TimeRemaining = match.TimeRemaining,
            WinnerNetworkId = match.WinnerNetworkId,
        };

        private static void Broadcast<T>(EntityCommandBuffer ecb, T rpc) where T : unmanaged, IRpcCommand
            => Send(ecb, Entity.Null, rpc);

        /// <summary><paramref name="connection"/> = <see cref="Entity.Null"/> sends to every connection.</summary>
        private static void Send<T>(EntityCommandBuffer ecb, Entity connection, T rpc) where T : unmanaged, IRpcCommand
        {
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, rpc);
            ecb.AddComponent(entity, new SendRpcCommandRequest { TargetConnection = connection });
        }
    }
}
