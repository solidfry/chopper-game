using System;
using Chopper.Simulation.Session;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Chopper.Simulation.Match
{
    /// <summary>The rules set the server plays (plan section 7). Picked once at server start (<see cref="MatchModeSource"/>).</summary>
    public enum MatchMode : byte
    {
        FreeForAll = 0,
        TeamDeathmatch = 1,
        /// <summary>Two teams, one nuke: carry it home, arm it, fly it into the enemy base (plan section 7).</summary>
        CaptureTheNuke = 2,
    }

    /// <summary>
    /// The side of a player: 1 to <see cref="MatchRules.TeamCount"/> (any number up to <see cref="TeamRules.MaxTeams"/>).
    /// On the player entity (connection or server bot, server only) and on its chopper ghost (sent to clients for
    /// markers and colours). <see cref="TeamRules.None"/> in Free For All: everyone is an enemy.
    /// </summary>
    public struct Team : IComponentData
    {
        [GhostField] public byte Value;
    }

    /// <summary>Pure team rules, shared by scoring, damage, targeting, spawns and presentation (EditMode tests).</summary>
    public static class TeamRules
    {
        public const byte None = 0;
        /// <summary>Most teams a match can have (team scores live in a <see cref="FixedList64Bytes{T}"/>).</summary>
        public const int MaxTeams = 8;

        /// <summary>The team count a match uses: 2 to <see cref="MaxTeams"/>.</summary>
        public static int ClampTeamCount(int count) => math.clamp(count, 2, MaxTeams);

        public static bool IsTeamMode(MatchMode mode) => mode != MatchMode.FreeForAll;

        /// <summary>Same non-zero team. Free For All (team 0) has no allies.</summary>
        public static bool AreAllies(byte a, byte b) => a != None && a == b;

        public static bool AreEnemies(byte a, byte b) => !AreAllies(a, b);

        /// <summary>Damage from a team mate is ignored (friendly fire off); self damage (crashes, own splash) still counts.</summary>
        public static bool BlocksDamage(byte attacker, byte victim, bool self) => !self && AreAllies(attacker, victim);

        /// <summary>
        /// The team a new player joins: the one with the fewest players, the lowest number on a tie.
        /// <paramref name="members"/>[i] = players in team i + 1; its length is the team count.
        /// </summary>
        public static byte JoinTeam(MatchMode mode, in FixedList64Bytes<int> members)
        {
            if (!IsTeamMode(mode) || members.Length == 0) return None;
            var best = 0;
            for (var i = 1; i < members.Length; i++)
                if (members[i] < members[best]) best = i;
            return (byte)(best + 1);
        }

        /// <summary>The team with the most kills (<paramref name="scores"/>[i] = team i + 1); <see cref="None"/> when the top is shared (draw).</summary>
        public static byte Leader(in FixedList64Bytes<int> scores)
        {
            var best = -1;
            var shared = false;
            for (var i = 0; i < scores.Length; i++)
            {
                if (best < 0 || scores[i] > scores[best]) { best = i; shared = false; }
                else if (scores[i] == scores[best]) shared = true;
            }
            return best < 0 || shared ? None : (byte)(best + 1);
        }

        /// <summary>A list of <paramref name="count"/> zeros (team scores or member counts).</summary>
        public static FixedList64Bytes<int> Zeros(int count)
        {
            var list = new FixedList64Bytes<int>();
            for (var i = 0; i < math.min(count, MaxTeams); i++) list.Add(0);
            return list;
        }

        /// <summary>
        /// A kill counts for the killer's team in Team Deathmatch when the victim is an enemy. Capture the Nuke counts
        /// rounds instead (<see cref="NukeRoundWon"/>), not kills.
        /// </summary>
        public static bool ScoresForTeam(MatchMode mode, byte killer, byte victim) =>
            mode == MatchMode.TeamDeathmatch && killer != None && killer != victim;

        /// <summary>"tdm" / "teamdeathmatch", "ctn" / "capturethenuke" (case ignored); everything else is Free For All.</summary>
        public static MatchMode ParseMode(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return MatchMode.FreeForAll;
            var value = text.Trim();
            if (value.Equals("tdm", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("teamdeathmatch", StringComparison.OrdinalIgnoreCase))
                return MatchMode.TeamDeathmatch;
            if (value.Equals("ctn", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("capturethenuke", StringComparison.OrdinalIgnoreCase))
                return MatchMode.CaptureTheNuke;
            return MatchMode.FreeForAll;
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
