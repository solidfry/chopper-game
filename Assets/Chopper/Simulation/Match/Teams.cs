using System;
using Chopper.Simulation.Session;
using Unity.Entities;
using Unity.NetCode;

namespace Chopper.Simulation.Match
{
    /// <summary>The rules set the server plays (plan section 7). Picked once at server start (<see cref="MatchModeSource"/>).</summary>
    public enum MatchMode : byte
    {
        FreeForAll = 0,
        TeamDeathmatch = 1,
    }

    /// <summary>
    /// The side of a player. On the player entity (connection or server bot, server only) and on its chopper ghost (sent
    /// to clients for markers and colours). <see cref="TeamRules.None"/> in Free For All: everyone is an enemy.
    /// </summary>
    public struct Team : IComponentData
    {
        [GhostField] public byte Value;
    }

    /// <summary>Pure team rules, shared by scoring, damage, targeting, spawns and presentation (EditMode tests).</summary>
    public static class TeamRules
    {
        public const byte None = 0;
        public const byte A = 1;
        public const byte B = 2;

        public static bool IsTeamMode(MatchMode mode) => mode == MatchMode.TeamDeathmatch;

        /// <summary>Same non-zero team. Free For All (team 0) has no allies.</summary>
        public static bool AreAllies(byte a, byte b) => a != None && a == b;

        public static bool AreEnemies(byte a, byte b) => !AreAllies(a, b);

        /// <summary>Damage from a team mate is ignored (friendly fire off); self damage (crashes, own splash) still counts.</summary>
        public static bool BlocksDamage(byte attacker, byte victim, bool self) => !self && AreAllies(attacker, victim);

        /// <summary>The team a new player joins: the smaller one, A on a tie.</summary>
        public static byte JoinTeam(MatchMode mode, int countA, int countB) =>
            !IsTeamMode(mode) ? None : countB < countA ? B : A;

        /// <summary>The team with more kills; <see cref="None"/> on a tie (draw).</summary>
        public static byte Leader(int scoreA, int scoreB) => scoreA > scoreB ? A : scoreB > scoreA ? B : None;

        /// <summary>A kill counts for the killer's team when it is a team mode and the victim is an enemy.</summary>
        public static bool ScoresForTeam(MatchMode mode, byte killer, byte victim) =>
            IsTeamMode(mode) && killer != None && killer != victim;

        /// <summary>"tdm", "teamdeathmatch" or "TeamDeathmatch" (case ignored); everything else is Free For All.</summary>
        public static MatchMode ParseMode(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return MatchMode.FreeForAll;
            var value = text.Trim();
            return value.Equals("tdm", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("teamdeathmatch", StringComparison.OrdinalIgnoreCase)
                ? MatchMode.TeamDeathmatch
                : MatchMode.FreeForAll;
        }
    }

    /// <summary>
    /// Server: the mode of this server process. <c>-mode tdm</c> on the command line, else the environment variable
    /// <c>MATCH_MODE</c> (the Edgegap allocator sets it from the matchmaker queue), else Free For All.
    /// </summary>
    public static class MatchModeSource
    {
        public static MatchMode Read()
        {
            var arg = ServerArgs.String("-mode");
            if (!string.IsNullOrEmpty(arg)) return TeamRules.ParseMode(arg);
            return TeamRules.ParseMode(Environment.GetEnvironmentVariable("MATCH_MODE"));
        }
    }
}
