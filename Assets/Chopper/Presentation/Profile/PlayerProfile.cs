using System;
using System.Collections.Generic;

namespace Chopper.Presentation.Profile
{
    /// <summary>
    /// The player's lifetime record: XP, totals over all matches, how often each award was won, achievements, and the
    /// last few matches. Plain serializable data (JsonUtility), so the same shape can live in PlayerPrefs now and in
    /// Cloud Save later (plan section 6: the game server posts match results to Cloud Code, which writes this record;
    /// clients only read it). Field names are the save format: rename only with a <see cref="Version"/> step.
    /// </summary>
    [Serializable]
    public sealed class PlayerProfile
    {
        public const int CurrentVersion = 1;
        /// <summary>Recent matches kept, newest first.</summary>
        public const int RecentLimit = 10;

        public int version = CurrentVersion;
        public int totalXp;

        public int matches;
        public int wins;
        public int draws;

        public int kills;
        public int deaths;
        public int shots;
        public int hits;
        public int bestStreak;
        public int firstBloods;
        public int crashes;
        public int boundsDeaths;
        public int rams;
        public int missileHits;
        public int missilesDodged;
        public int flares;
        public int repairs;
        public int abilities;
        public float damageDealt;
        public float damageTaken;
        public float repaired;
        public float longestKill;

        public List<AwardCount> awards = new();
        public List<AchievementUnlock> achievements = new();
        public List<MatchRecord> recent = new();

        public float Accuracy => shots > 0 ? Math.Min(1f, (float)hits / shots) : 0f;
        /// <summary>Kills per death; with no deaths, the kills.</summary>
        public float KillDeathRatio => deaths > 0 ? (float)kills / deaths : kills;
        public float WinRate => matches > 0 ? (float)wins / matches : 0f;

        public int AwardCountOf(string title)
        {
            foreach (var award in awards)
                if (award.title == title) return award.count;
            return 0;
        }

        public bool HasAchievement(string id)
        {
            foreach (var unlock in achievements)
                if (unlock.id == id) return true;
            return false;
        }
    }

    [Serializable]
    public struct AwardCount
    {
        public string title;
        public int count;
    }

    [Serializable]
    public struct AchievementUnlock
    {
        public string id;
        /// <summary>UTC, seconds since 1970.</summary>
        public long unixTime;
    }

    /// <summary>One finished match, for the recent list.</summary>
    [Serializable]
    public sealed class MatchRecord
    {
        public long unixTime;
        public bool won;
        public bool draw;
        /// <summary>1 = first.</summary>
        public int place;
        public int players;
        public int kills;
        public int deaths;
        public int score;
        public int xp;
        /// <summary>Pilot call sign flown ("" without one).</summary>
        public string pilot = "";
        public List<string> awards = new();
    }
}
