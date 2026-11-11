using System;
using System.Collections.Generic;

namespace Chopper.Presentation.Profile
{
    /// <summary>One lifetime goal: a value read from the profile and the target it must reach.</summary>
    public readonly struct AchievementDefinition
    {
        /// <summary>Stable save key (also the future Steam achievement API name).</summary>
        public readonly string Id;
        public readonly string Title;
        public readonly string Description;
        public readonly float Target;
        public readonly Func<PlayerProfile, float> Value;

        public AchievementDefinition(string id, string title, string description, float target, Func<PlayerProfile, float> value)
        {
            Id = id;
            Title = title;
            Description = description;
            Target = target;
            Value = value;
        }

        /// <summary>0..1 toward the target.</summary>
        public float Progress(PlayerProfile profile) => Target > 0f ? Math.Min(1f, Math.Max(0f, Value(profile) / Target)) : 1f;
    }

    /// <summary>
    /// The achievement list (edit-mode tests: ProfileTests). Immutable; ids are save keys, so never rename one. Later the
    /// same ids can be mirrored as Steam achievements (plan section 6).
    /// </summary>
    public static class Achievements
    {
        public static readonly IReadOnlyList<AchievementDefinition> All = new[]
        {
            new AchievementDefinition("first_sortie", "FIRST SORTIE", "Finish a match.", 1, p => p.matches),
            new AchievementDefinition("veteran", "VETERAN", "Finish 50 matches.", 50, p => p.matches),
            new AchievementDefinition("victor", "VICTOR", "Win 10 matches.", 10, p => p.wins),
            new AchievementDefinition("centurion", "CENTURION", "Shoot down 100 choppers.", 100, p => p.kills),
            new AchievementDefinition("warlord", "WARLORD", "Shoot down 1000 choppers.", 1000, p => p.kills),
            new AchievementDefinition("unstoppable", "UNSTOPPABLE", "10 kills in one life.", 10, p => p.bestStreak),
            new AchievementDefinition("long_arm", "LONG ARM", "A kill from 800 m.", 800, p => p.longestKill),
            new AchievementDefinition("marksman", "MARKSMAN", "Land 5000 hits.", 5000, p => p.hits),
            new AchievementDefinition("heavy_hitter", "HEAVY HITTER", "Deal 50 000 damage.", 50000, p => p.damageDealt),
            new AchievementDefinition("guided", "GUIDED", "50 missiles on target.", 50, p => p.missileHits),
            new AchievementDefinition("chaff_king", "CHAFF KING", "Flare off 25 missiles.", 25, p => p.missilesDodged),
            new AchievementDefinition("field_medic", "FIELD MEDIC", "Mend 5000 hull in the field.", 5000, p => p.repaired),
            new AchievementDefinition("bulldozer", "BULLDOZER", "Ram 50 choppers.", 50, p => p.rams),
            new AchievementDefinition("decorated", "DECORATED", "Win 10 different awards.", 10, p => p.awards.Count),
            new AchievementDefinition("frequent_flyer", "FREQUENT FLYER", "Crash 25 times. The ground says hi.", 25, p => p.crashes),
        };

        /// <summary>Unlocks every achievement the profile has now reached; returns the ids unlocked by this call.</summary>
        public static List<string> Unlock(PlayerProfile profile, long unixTime)
        {
            var unlocked = new List<string>();
            foreach (var achievement in All)
            {
                if (profile.HasAchievement(achievement.Id) || achievement.Value(profile) < achievement.Target) continue;
                profile.achievements.Add(new AchievementUnlock { id = achievement.Id, unixTime = unixTime });
                unlocked.Add(achievement.Id);
            }
            return unlocked;
        }
    }
}
