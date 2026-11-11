using System;
using System.Collections.Generic;
using Chopper.Presentation.Hangar;
using Chopper.Presentation.Results;
using Chopper.Simulation.Match;
using UnityEngine;

namespace Chopper.Presentation.Profile
{
    /// <summary>One finished match as the profile records it.</summary>
    public readonly struct MatchOutcome
    {
        public readonly PlayerMatchStats Stats;
        public readonly IReadOnlyList<Award> Awards;
        public readonly bool Won;
        public readonly bool Draw;
        public readonly int Place;
        public readonly int Players;
        public readonly int Xp;
        public readonly int Score;
        public readonly string Pilot;

        public MatchOutcome(PlayerMatchStats stats, IReadOnlyList<Award> awards, bool won, bool draw, int place, int players, int xp,
            int score, string pilot)
        {
            Stats = stats;
            Awards = awards;
            Won = won;
            Draw = draw;
            Place = place;
            Players = players;
            Xp = xp;
            Score = score;
            Pilot = pilot ?? "";
        }
    }

    /// <summary>How a match changes the profile. Pure (edit-mode tests: ProfileTests).</summary>
    public static class ProfileRules
    {
        /// <summary>Adds the match to the totals, award counts and recent list, then unlocks achievements (returned).</summary>
        public static List<string> Record(PlayerProfile p, in MatchOutcome match, long unixTime)
        {
            var s = match.Stats;
            p.totalXp += Math.Max(0, match.Xp);
            p.matches++;
            if (match.Won) p.wins++;
            if (match.Draw) p.draws++;
            p.kills += s.Kills;
            p.deaths += s.Deaths;
            p.shots += s.Shots;
            p.hits += s.Hits;
            p.bestStreak = Math.Max(p.bestStreak, s.BestStreak);
            if (s.FirstBlood) p.firstBloods++;
            p.crashes += s.Crashes;
            p.boundsDeaths += s.BoundsDeaths;
            p.rams += s.Rams;
            p.missileHits += s.MissileHits;
            p.missilesDodged += s.MissilesDodged;
            p.flares += s.Flares;
            p.repairs += s.Repairs;
            p.abilities += s.Abilities;
            p.damageDealt += s.DamageDealt;
            p.damageTaken += s.DamageTaken;
            p.repaired += s.Repaired;
            p.longestKill = Math.Max(p.longestKill, s.LongestKill);

            var record = new MatchRecord
            {
                unixTime = unixTime,
                won = match.Won,
                draw = match.Draw,
                place = match.Place,
                players = match.Players,
                kills = s.Kills,
                deaths = s.Deaths,
                score = match.Score,
                xp = match.Xp,
                pilot = match.Pilot,
            };
            if (match.Awards != null)
                foreach (var award in match.Awards)
                {
                    record.awards.Add(award.Title);
                    AddAward(p, award.Title);
                }
            p.recent.Insert(0, record);
            if (p.recent.Count > PlayerProfile.RecentLimit) p.recent.RemoveRange(PlayerProfile.RecentLimit, p.recent.Count - PlayerProfile.RecentLimit);

            return Achievements.Unlock(p, unixTime);
        }

        private static void AddAward(PlayerProfile p, string title)
        {
            for (var i = 0; i < p.awards.Count; i++)
            {
                if (p.awards[i].title != title) continue;
                var count = p.awards[i];
                count.count++;
                p.awards[i] = count;
                return;
            }
            p.awards.Add(new AwardCount { title = title, count = 1 });
        }
    }

    /// <summary>Where the profile is kept. Local now; Cloud Save (read-only for the client) later.</summary>
    public interface IProfileStore
    {
        PlayerProfile Load();
        void Save(PlayerProfile profile);
    }

    /// <summary>The profile as JSON in PlayerPrefs (through <see cref="IHangarPrefs"/>, so tests use a dictionary).</summary>
    public sealed class PrefsProfileStore : IProfileStore
    {
        private const string Key = "Chopper.Profile";
        /// <summary>The first results screen kept only an XP total here: read once into the profile.</summary>
        private const string OldXpKey = "Chopper.Profile.Xp";
        private readonly IHangarPrefs _prefs;

        public PrefsProfileStore(IHangarPrefs prefs) => _prefs = prefs;

        public PlayerProfile Load()
        {
            var json = _prefs.GetString(Key, "");
            PlayerProfile profile = null;
            if (!string.IsNullOrEmpty(json))
            {
                try { profile = JsonUtility.FromJson<PlayerProfile>(json); }
                catch (ArgumentException e) { Debug.LogWarning($"Profile save could not be read ({e.Message}); starting a new one."); }
            }
            if (profile != null) return profile;
            return new PlayerProfile { totalXp = Math.Max(0, _prefs.GetInt(OldXpKey, 0)) };
        }

        public void Save(PlayerProfile profile)
        {
            _prefs.SetString(Key, JsonUtility.ToJson(profile));
            _prefs.Save();
        }
    }

    /// <summary>
    /// Who the player is: name and avatar. Local now ("OPERATOR", no avatar: the screens show the pilot instead); a
    /// Steam version later (Steamworks.NET: persona name, large friend avatar as a texture; plan section 6).
    /// </summary>
    public interface IPlayerIdentity
    {
        string DisplayName { get; }
        /// <summary>The account picture, or null (screens fall back to the selected pilot's portrait).</summary>
        Texture2D Avatar { get; }
        /// <summary>"LOCAL", "STEAM": shown small on the profile.</summary>
        string Source { get; }
        /// <summary>Name or avatar arrived or changed (Steam avatars load later than the name).</summary>
        event Action Changed;
    }

    public sealed class LocalIdentity : IPlayerIdentity
    {
        public string DisplayName => "OPERATOR";
        public Texture2D Avatar => null;
        public string Source => "LOCAL";
        public event Action Changed { add { } remove { } }
    }

    /// <summary>
    /// The player's profile: loads it, records each match (<see cref="MatchResultsModel"/> calls
    /// <see cref="RecordMatch"/> once per match) and saves it. Online, the server will write the record and this only
    /// reads it; the screens do not change.
    /// </summary>
    public sealed class ProfileService
    {
        private readonly IProfileStore _store;
        private PlayerProfile _profile;

        public ProfileService(IProfileStore store) => _store = store;

        public PlayerProfile Profile => _profile ??= _store.Load() ?? new PlayerProfile();
        public int TotalXp => Profile.totalXp;
        public int Level => PilotLevels.Level(TotalXp, out _, out _);

        /// <summary>0..1 into the current level.</summary>
        public float LevelProgress
        {
            get
            {
                PilotLevels.Level(TotalXp, out var into, out var needed);
                return needed > 0 ? (float)into / needed : 0f;
            }
        }

        public event Action Changed;

        /// <summary>Records and saves the match; returns the achievements it unlocked.</summary>
        public IReadOnlyList<string> RecordMatch(in MatchOutcome match)
        {
            var unlocked = ProfileRules.Record(Profile, match, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            _store.Save(Profile);
            Changed?.Invoke();
            return unlocked;
        }
    }
}
