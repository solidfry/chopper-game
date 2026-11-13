using System.Collections.Generic;
using Chopper.Simulation.Match;
using Chopper.Simulation.Progression;
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
        /// <summary>The default rates (<see cref="XpRates.Default"/>). The game uses the progression settings asset.</summary>
        public const int MatchComplete = 150;
        public const int Victory = 300;
        public const int PerKill = 100;
        /// <summary>XP per point of damage dealt.</summary>
        public const float PerDamage = 0.2f;

        /// <summary>With the default rates.</summary>
        public static int Lines(in PlayerMatchStats stats, IReadOnlyList<Award> awards, bool won, List<XpLine> lines) =>
            Lines(stats, awards, won, lines, XpRates.Default);

        /// <summary>Fills <paramref name="lines"/> (cleared) and returns the total.</summary>
        public static int Lines(in PlayerMatchStats stats, IReadOnlyList<Award> awards, bool won, List<XpLine> lines, in XpRates rates)
        {
            lines.Clear();
            if (rates.MatchComplete > 0) lines.Add(new XpLine("MATCH COMPLETE", rates.MatchComplete));
            if (won && rates.Victory > 0) lines.Add(new XpLine("VICTORY", rates.Victory));
            if (stats.Kills > 0 && rates.PerKill > 0) lines.Add(new XpLine($"KILLS x{stats.Kills}", stats.Kills * rates.PerKill));
            var damage = Mathf.RoundToInt(stats.DamageDealt * rates.PerDamage);
            if (damage > 0) lines.Add(new XpLine($"DAMAGE {stats.DamageDealt:0}", damage));
            var awardXp = 0;
            foreach (var award in awards) awardXp += award.Xp;
            if (awardXp > 0) lines.Add(new XpLine(awards.Count == 1 ? "AWARD" : $"AWARDS x{awards.Count}", awardXp));

            var total = 0;
            foreach (var line in lines) total += line.Amount;
            return total;
        }
    }

    /// <summary>
    /// Account level from total XP with the default numbers and no cap (1000, then +250 per level). The results and
    /// profile screens still use it; the profile service uses the settings curve
    /// (<see cref="LevelCurve"/>). Keep the account curve at its default until the screens move over.
    /// </summary>
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
