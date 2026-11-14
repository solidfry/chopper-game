using System;
using System.Collections.Generic;
using Chopper.Simulation.Loadout;

namespace Chopper.Simulation.Progression
{
    /// <summary>The three progression tracks (plan section 6a-1). One match XP number feeds all three.</summary>
    public enum ProgressTrack : byte
    {
        /// <summary>All match XP. Unlocks chassis, pilots and weapon families.</summary>
        Account = 0,
        /// <summary>XP by flight time on a <see cref="ChassisClass"/>. Unlocks that class's part chain tiers and skins.</summary>
        Chassis = 1,
        /// <summary>XP by flight time with a pilot. Unlocks cosmetics only.</summary>
        Pilot = 2,
    }

    /// <summary>
    /// XP needed per level: the first level costs <see cref="FirstLevelXp"/>, each next level <see cref="StepXp"/> more.
    /// Levels start at 1. Only XP is saved; the level is always worked out from it, so the curve can change later.
    /// </summary>
    [Serializable]
    public struct LevelCurve
    {
        public int FirstLevelXp;
        public int StepXp;
        /// <summary>Highest level; 0 = no cap.</summary>
        public int MaxLevel;

        public LevelCurve(int firstLevelXp, int stepXp, int maxLevel)
        {
            FirstLevelXp = firstLevelXp;
            StepXp = stepXp;
            MaxLevel = maxLevel;
        }

        /// <summary>Same numbers as the first results screen (1000, then +250 per level).</summary>
        public static LevelCurve Account => new(1000, 250, 50);
        public static LevelCurve Chassis => new(800, 200, 20);
        public static LevelCurve Pilot => new(800, 200, 15);

        /// <summary>XP to go from <paramref name="level"/> to the next.</summary>
        public int Cost(int level) => FirstLevelXp + StepXp * (Math.Max(level, 1) - 1);

        public int Level(int totalXp) => Level(totalXp, out _, out _);

        /// <summary>
        /// Level (from 1) for <paramref name="totalXp"/>, the XP into it and the XP it needs. At the cap both are 0.
        /// </summary>
        public int Level(int totalXp, out int into, out int needed)
        {
            var level = 1;
            into = Math.Max(totalXp, 0);
            needed = Cost(level);
            while ((MaxLevel <= 0 || level < MaxLevel) && needed > 0 && into >= needed)
            {
                into -= needed;
                level++;
                needed = Cost(level);
            }
            if (MaxLevel > 0 && level >= MaxLevel)
            {
                into = 0;
                needed = 0;
            }
            return level;
        }

        public void CollectProblems(string owner, string label, List<string> problems)
        {
            if (FirstLevelXp <= 0) problems.Add($"{owner}: {label} curve: the first level must cost more than 0 XP.");
            if (StepXp < 0) problems.Add($"{owner}: {label} curve: the step must not be below 0.");
            if (MaxLevel < 0) problems.Add($"{owner}: {label} curve: the highest level must not be below 0 (0 = no cap).");
        }
    }

    /// <summary>XP for the parts of a match. Data, so the rates change without code.</summary>
    [Serializable]
    public struct XpRates
    {
        public int MatchComplete;
        public int Victory;
        public int PerKill;
        /// <summary>XP per point of damage dealt.</summary>
        public float PerDamage;
        /// <summary>A kill on a server bot gives this part of <see cref="PerKill"/> (0..1).</summary>
        public float BotKillFactor;

        public static XpRates Default => new() { MatchComplete = 150, Victory = 300, PerKill = 100, PerDamage = 0.2f, BotKillFactor = 0.5f };
    }

    /// <summary>Time one player flew one chassis class with one pilot in a match.</summary>
    public readonly struct FlightShare
    {
        public readonly ChassisClass Class;
        public readonly ushort PilotId;
        public readonly float Seconds;

        public FlightShare(ChassisClass chassisClass, ushort pilotId, float seconds)
        {
            Class = chassisClass;
            PilotId = pilotId;
            Seconds = seconds;
        }
    }

    /// <summary>Splits match XP by flight time. Pure (edit-mode tests: ProgressionTests).</summary>
    public static class MasterySplit
    {
        /// <summary>
        /// Fills <paramref name="shares"/> (cleared) with one whole number per entry of <paramref name="seconds"/>.
        /// The shares add up to <paramref name="xp"/> exactly: the XP left after rounding down goes to the longest time.
        /// With no time at all, every share is 0.
        /// </summary>
        public static void Split(int xp, IReadOnlyList<float> seconds, List<int> shares)
        {
            shares.Clear();
            var total = 0.0;
            var longest = -1;
            for (var i = 0; i < seconds.Count; i++)
            {
                var time = Math.Max(0f, seconds[i]);
                total += time;
                if (time > 0f && (longest < 0 || time > seconds[longest])) longest = i;
            }

            var given = 0;
            for (var i = 0; i < seconds.Count; i++)
            {
                var share = total > 0.0 && xp > 0 ? (int)Math.Floor(xp * (Math.Max(0f, seconds[i]) / total)) : 0;
                shares.Add(share);
                given += share;
            }
            if (longest >= 0 && xp > given) shares[longest] += xp - given;
        }

        /// <summary>
        /// Adds up the time per key (chassis class, or pilot id) into two lists of the same length (both cleared).
        /// Key 0 (no class, no pilot) is left out.
        /// </summary>
        public static void Collect(IReadOnlyList<FlightShare> flown, ProgressTrack track, List<int> keys, List<float> seconds)
        {
            keys.Clear();
            seconds.Clear();
            if (flown == null) return;
            foreach (var share in flown)
            {
                var key = track == ProgressTrack.Pilot ? share.PilotId : (int)share.Class;
                if (key == 0 || share.Seconds <= 0f) continue;
                var at = keys.IndexOf(key);
                if (at < 0)
                {
                    keys.Add(key);
                    seconds.Add(share.Seconds);
                }
                else seconds[at] += share.Seconds;
            }
        }
    }
}
