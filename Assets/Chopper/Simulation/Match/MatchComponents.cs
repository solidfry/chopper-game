using Unity.Entities;
using Unity.NetCode;

namespace Chopper.Simulation.Match
{
    public enum MatchPhase : byte
    {
        InProgress = 0,
        PostGame = 1,
    }

    /// <summary>Match rules (both modes). Singleton from <see cref="MatchRulesAuthoring"/>, or <see cref="Default"/>.</summary>
    public struct MatchRules : IComponentData
    {
        public float TimeLimit;
        /// <summary>Free For All: one player's kills that end the match.</summary>
        public int ScoreLimit;
        /// <summary>Team Deathmatch: one team's kills that end the match.</summary>
        public int TeamScoreLimit;
        /// <summary>Team modes: number of teams, 2 to <see cref="TeamRules.MaxTeams"/>.</summary>
        public int TeamCount;
        /// <summary>Seconds the results show before the next match starts.</summary>
        public float PostGameDuration;
        /// <summary>A crash or bounds death this soon after a hit counts as the attacker's kill.</summary>
        public float KillCreditSeconds;

        public static MatchRules Default => new()
        {
            TimeLimit = 600f,
            ScoreLimit = 20,
            TeamScoreLimit = 40,
            TeamCount = 2,
            PostGameDuration = 20f,
            KillCreditSeconds = 5f,
        };
    }

    /// <summary>Server only singleton. Clients get it through <see cref="MatchStateRpc"/>.</summary>
    public struct MatchState : IComponentData
    {
        public MatchPhase Phase;
        public float TimeRemaining;
        /// <summary>0 = no winner (draw or match still running).</summary>
        public int WinnerNetworkId;
        /// <summary>Set once at server start (<see cref="MatchModeSource"/>); kept across matches.</summary>
        public MatchMode Mode;
        /// <summary>Team modes: kills of each team ([i] = team i + 1; the length is the team count). Empty in Free For All.</summary>
        public Unity.Collections.FixedList64Bytes<int> TeamScores;
        /// <summary>Team Deathmatch winner; <see cref="TeamRules.None"/> = draw or still running.</summary>
        public byte WinnerTeam;
    }

    /// <summary>Server only, on a player entity (a connection or a server bot).</summary>
    public struct PlayerScore : IComponentData
    {
        public int Kills;
        public int Deaths;
    }

    /// <summary>Server only. Stays after the connection (or bot) is destroyed, so the server can tell clients who left.</summary>
    public struct PlayerScoreCleanup : ICleanupComponentData
    {
        public int NetworkId;
    }

    /// <summary>Server only, one entity per kill. Made by the damage system, used by <see cref="MatchScoreSystem"/>.</summary>
    public struct KillEvent : IComponentData
    {
        /// <summary>0 = no killer (crash or bounds with no recent attacker).</summary>
        public int KillerNetworkId;
        public int VictimNetworkId;
    }

    public struct ScoreRpc : IRpcCommand
    {
        public int NetworkId;
        public int Kills;
        public int Deaths;
        /// <summary><see cref="TeamRules"/> team of that player (0 in Free For All).</summary>
        public byte Team;
    }

    public struct PlayerLeftRpc : IRpcCommand
    {
        public int NetworkId;
    }

    public struct KillFeedRpc : IRpcCommand
    {
        public int KillerNetworkId;
        public int VictimNetworkId;
    }

    /// <summary>Sent when the phase changes and to players who join. Clients count the time down themselves.</summary>
    public struct MatchStateRpc : IRpcCommand
    {
        /// <summary><see cref="MatchPhase"/> as a byte.</summary>
        public byte Phase;
        public float TimeRemaining;
        public int WinnerNetworkId;
        /// <summary><see cref="MatchMode"/> as a byte.</summary>
        public byte Mode;
        /// <summary>Team modes: number of teams (0 in Free For All). Scores come in <see cref="TeamScoreRpc"/>.</summary>
        public byte TeamCount;
        public byte WinnerTeam;
    }

    /// <summary>One team's kills: sent when it changes, and all of them to a player who joins.</summary>
    public struct TeamScoreRpc : IRpcCommand
    {
        public byte Team;
        public int Score;
    }
}
