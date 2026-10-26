using Unity.Entities;
using Unity.NetCode;

namespace Chopper.Simulation.Match
{
    public enum MatchPhase : byte
    {
        InProgress = 0,
        PostGame = 1,
    }

    /// <summary>Free For All rules. Singleton from <see cref="MatchRulesAuthoring"/>, or <see cref="Default"/>.</summary>
    public struct MatchRules : IComponentData
    {
        public float TimeLimit;
        public int ScoreLimit;
        /// <summary>Seconds the results show before the next match starts.</summary>
        public float PostGameDuration;
        /// <summary>A crash or bounds death this soon after a hit counts as the attacker's kill.</summary>
        public float KillCreditSeconds;

        public static MatchRules Default => new()
        {
            TimeLimit = 600f,
            ScoreLimit = 20,
            PostGameDuration = 10f,
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
    }

    /// <summary>Server only, on a connection entity.</summary>
    public struct PlayerScore : IComponentData
    {
        public int Kills;
        public int Deaths;
    }

    /// <summary>Server only. Stays after the connection is destroyed, so the server can tell clients who left.</summary>
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
    }
}
