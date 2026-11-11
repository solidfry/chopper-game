using System.Collections.Generic;
using Chopper.Simulation.Match;
using UnityEngine;

namespace Chopper.Presentation.Results
{
    /// <summary>One line of the XP count-up ("KILLS x4  +400").</summary>
    public readonly struct XpLine
    {
        public readonly string Label;
        public readonly int Amount;

        public XpLine(string label, int amount)
        {
            Label = label;
            Amount = amount;
        }
    }

    /// <summary>
    /// XP for one match, from the server's stats and the awards (edit-mode tests: MatchResultsTests). The profile
    /// (<see cref="Profile.ProfileService"/>) keeps the total.
    /// </summary>
    public static class MatchXp
    {
        public const int MatchComplete = 150;
        public const int Victory = 300;
        public const int PerKill = 100;
        /// <summary>XP per point of damage dealt.</summary>
        public const float PerDamage = 0.2f;

        /// <summary>Fills <paramref name="lines"/> (cleared) and returns the total.</summary>
        public static int Lines(in PlayerMatchStats stats, IReadOnlyList<Award> awards, bool won, List<XpLine> lines)
        {
            lines.Clear();
            lines.Add(new XpLine("MATCH COMPLETE", MatchComplete));
            if (won) lines.Add(new XpLine("VICTORY", Victory));
            if (stats.Kills > 0) lines.Add(new XpLine($"KILLS x{stats.Kills}", stats.Kills * PerKill));
            var damage = Mathf.RoundToInt(stats.DamageDealt * PerDamage);
            if (damage > 0) lines.Add(new XpLine($"DAMAGE {stats.DamageDealt:0}", damage));
            var awardXp = 0;
            foreach (var award in awards) awardXp += award.Xp;
            if (awardXp > 0) lines.Add(new XpLine(awards.Count == 1 ? "AWARD" : $"AWARDS x{awards.Count}", awardXp));

            var total = 0;
            foreach (var line in lines) total += line.Amount;
            return total;
        }
    }

    /// <summary>Pilot level from total XP. Each level costs more: 1000, then +250 per level.</summary>
    public static class PilotLevels
    {
        public const int FirstLevelXp = 1000;
        public const int StepXp = 250;

        public static int Cost(int level) => FirstLevelXp + StepXp * (Mathf.Max(level, 1) - 1);

        /// <summary>Level (from 1) for <paramref name="totalXp"/>, and the XP into it and needed for the next.</summary>
        public static int Level(int totalXp, out int into, out int needed)
        {
            var level = 1;
            into = Mathf.Max(totalXp, 0);
            needed = Cost(level);
            while (into >= needed)
            {
                into -= needed;
                level++;
                needed = Cost(level);
            }
            return level;
        }
    }
}
