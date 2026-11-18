using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Chopper.Simulation.Session;
using Unity.NetCode;

namespace Chopper.Simulation.Match
{
    /// <summary>
    /// Server only. Keeps each player's kills and deaths, runs the match timer, ends the match at the score or time
    /// limit, then starts the next one. Tells clients through RPCs (score, kill feed, match state).
    /// A player is any entity with a <see cref="PlayerId"/>: a connection or a server bot.
    /// Team Deathmatch (<see cref="MatchState.Mode"/>): a new player joins the smaller team (<see cref="Team"/> on the
    /// player entity; the respawn copies it to the chopper), kills of enemies count for the killer's team, and the
    /// team limit or the time-out ends the match (<see cref="TeamRules"/>).
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct MatchScoreSystem : ISystem
    {
        // Not Burst: the mode comes from the command line or the environment (managed).
        public void OnCreate(ref SystemState state)
        {
            var rules = MatchRules.Default;
            var mode = MatchModeSource.Read();
            state.EntityManager.CreateSingleton(new MatchState
            {
                Phase = MatchPhase.InProgress,
                TimeRemaining = rules.TimeLimit,
                Mode = mode,
            });
            if (mode != MatchMode.FreeForAll) UnityEngine.Debug.Log($"[Match] Mode: {mode}.");
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var rules = SystemAPI.TryGetSingleton<MatchRules>(out var baked) ? baked : MatchRules.Default;
            var matchEntity = SystemAPI.GetSingletonEntity<MatchState>();
            var match = SystemAPI.GetComponent<MatchState>(matchEntity);
            var previousPhase = match.Phase;
            // Team modes: one score per team (a baked rules singleton can arrive after OnCreate).
            var teamCount = TeamRules.IsTeamMode(match.Mode) ? TeamRules.ClampTeamCount(rules.TeamCount) : 0;
            if (match.TeamScores.Length != teamCount) match.TeamScores = TeamRules.Zeros(teamCount);

            AddJoinedPlayers(ref state, match);
            RemoveLeftPlayers(ref state);

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            var connections = new NativeHashMap<int, Entity>(16, Allocator.Temp);
            foreach (var (networkId, connection) in SystemAPI.Query<RefRO<PlayerId>>().WithAll<PlayerScore>().WithEntityAccess())
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

                var killerTeam = TeamOf(ref state, killerConnection);
                var victimTeam = victimConnection != Entity.Null ? TeamOf(ref state, victimConnection) : TeamRules.None;
                // A kill of a team mate (a crash credited to a team mate) gives nothing.
                if (TeamRules.AreAllies(killerTeam, victimTeam)) continue;

                var killerScore = ChangeScore(ref state, ecb, killerConnection, killer, kills: 1, deaths: 0);
                if (TeamRules.ScoresForTeam(match.Mode, killerTeam, victimTeam))
                {
                    if (killerTeam > match.TeamScores.Length) continue;
                    var teamScore = ++match.TeamScores[killerTeam - 1];
                    Broadcast(ecb, new TeamScoreRpc { Team = killerTeam, Score = teamScore });
                    if (rules.TeamScoreLimit > 0 && teamScore >= rules.TeamScoreLimit)
                        EndMatch(ref match, rules, 0, killerTeam);
                }
                else if (!TeamRules.IsTeamMode(match.Mode) && killerScore.Kills >= rules.ScoreLimit)
                {
                    EndMatch(ref match, rules, killer, TeamRules.None);
                }
            }

            match.TimeRemaining -= SystemAPI.Time.DeltaTime;
            if (match.TimeRemaining <= 0f)
            {
                if (match.Phase == MatchPhase.InProgress)
                {
                    if (TeamRules.IsTeamMode(match.Mode)) EndMatch(ref match, rules, 0, TeamRules.Leader(match.TeamScores));
                    else EndMatch(ref match, rules, TopScorer(ref state), TeamRules.None);
                }
                else
                {
                    match = new MatchState
                    {
                        Phase = MatchPhase.InProgress,
                        TimeRemaining = rules.TimeLimit,
                        Mode = match.Mode,
                        TeamScores = TeamRules.Zeros(teamCount),
                    };
                    for (var team = 1; team <= teamCount; team++)
                        Broadcast(ecb, new TeamScoreRpc { Team = (byte)team, Score = 0 });
                    foreach (var (score, networkId, player) in SystemAPI.Query<RefRW<PlayerScore>, RefRO<PlayerId>>().WithEntityAccess())
                    {
                        score.ValueRW = default;
                        Broadcast(ecb, new ScoreRpc { NetworkId = networkId.ValueRO.Value, Team = TeamOf(ref state, player) });
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
            var members = TeamRules.Zeros(match.TeamScores.Length);
            foreach (var team in SystemAPI.Query<RefRO<Team>>().WithAll<PlayerId, PlayerScore>())
            {
                var value = team.ValueRO.Value;
                if (value != TeamRules.None && value <= members.Length) members[value - 1]++;
            }
            foreach (var (networkId, connection) in SystemAPI.Query<RefRO<PlayerId>>().WithNone<PlayerScore>().WithEntityAccess())
            {
                var id = networkId.ValueRO.Value;
                var joined = TeamRules.JoinTeam(match.Mode, members);
                if (joined != TeamRules.None) members[joined - 1]++;
                ecb.AddComponent<PlayerScore>(connection);
                ecb.AddComponent(connection, new PlayerScoreCleanup { NetworkId = id });
                ecb.AddComponent(connection, new Team { Value = joined });
                Broadcast(ecb, new ScoreRpc { NetworkId = id, Team = joined });
                // A server bot has no connection to send the scoreboard to.
                if (!SystemAPI.HasComponent<NetworkId>(connection)) continue;

                foreach (var (otherScore, otherId, other) in SystemAPI.Query<RefRO<PlayerScore>, RefRO<PlayerId>>().WithEntityAccess())
                {
                    Send(ecb, connection, new ScoreRpc
                    {
                        NetworkId = otherId.ValueRO.Value,
                        Kills = otherScore.ValueRO.Kills,
                        Deaths = otherScore.ValueRO.Deaths,
                        Team = TeamOf(ref state, other),
                    });
                }
                Send(ecb, connection, ToRpc(match));
                for (var team = 0; team < match.TeamScores.Length; team++)
                    Send(ecb, connection, new TeamScoreRpc { Team = (byte)(team + 1), Score = match.TeamScores[team] });
            }
            ecb.Playback(state.EntityManager);
        }

        private void RemoveLeftPlayers(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (cleanup, entity) in SystemAPI.Query<RefRO<PlayerScoreCleanup>>().WithNone<PlayerId>().WithEntityAccess())
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
            Broadcast(ecb, new ScoreRpc { NetworkId = networkId, Kills = score.Kills, Deaths = score.Deaths, Team = TeamOf(ref state, connection) });
            return score;
        }

        private byte TeamOf(ref SystemState state, Entity player) =>
            SystemAPI.HasComponent<Team>(player) ? SystemAPI.GetComponent<Team>(player).Value : TeamRules.None;

        /// <summary>Most kills wins; a tie at the top is a draw (0).</summary>
        private int TopScorer(ref SystemState state)
        {
            var best = 0;
            var bestKills = 0;
            var tie = false;
            foreach (var (score, networkId) in SystemAPI.Query<RefRO<PlayerScore>, RefRO<PlayerId>>())
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

        /// <summary>Keeps the mode and the team scores (the results show them); the next match clears them.</summary>
        private static void EndMatch(ref MatchState match, in MatchRules rules, int winner, byte winnerTeam)
        {
            match.Phase = MatchPhase.PostGame;
            match.TimeRemaining = rules.PostGameDuration;
            match.WinnerNetworkId = winner;
            match.WinnerTeam = winnerTeam;
        }

        private static MatchStateRpc ToRpc(in MatchState match) => new()
        {
            Phase = (byte)match.Phase,
            TimeRemaining = match.TimeRemaining,
            WinnerNetworkId = match.WinnerNetworkId,
            Mode = (byte)match.Mode,
            TeamCount = (byte)match.TeamScores.Length,
            WinnerTeam = match.WinnerTeam,
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
