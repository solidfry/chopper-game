using System;
using System.Collections.Generic;
using Chopper.Presentation.Hangar;
using Chopper.Presentation.Results;
using Chopper.Simulation.Loadout;
using Chopper.Simulation.Match;
using Chopper.Simulation.Progression;
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
        /// <summary>Time flown per chassis class and pilot: the mastery XP is split by it. Null = no mastery XP.</summary>
        public readonly IReadOnlyList<FlightShare> Flown;

        public MatchOutcome(PlayerMatchStats stats, IReadOnlyList<Award> awards, bool won, bool draw, int place, int players, int xp,
            int score, string pilot, IReadOnlyList<FlightShare> flown = null)
        {
            Flown = flown;
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
            AddMastery(p, match);
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

        /// <summary>The match XP goes to each chassis class and each pilot by the time flown (plan section 6a-1).</summary>
        private static void AddMastery(PlayerProfile p, in MatchOutcome match)
        {
            var xp = Math.Max(0, match.Xp);
            if (xp == 0 || match.Flown == null || match.Flown.Count == 0) return;
            var keys = new List<int>();
            var seconds = new List<float>();
            var shares = new List<int>();

            MasterySplit.Collect(match.Flown, ProgressTrack.Chassis, keys, seconds);
            MasterySplit.Split(xp, seconds, shares);
            p.chassisXp ??= new List<TrackXp>();
            for (var i = 0; i < keys.Count; i++) AddXp(p.chassisXp, keys[i], shares[i]);

            MasterySplit.Collect(match.Flown, ProgressTrack.Pilot, keys, seconds);
            MasterySplit.Split(xp, seconds, shares);
            p.pilotXp ??= new List<TrackXp>();
            for (var i = 0; i < keys.Count; i++) AddXp(p.pilotXp, keys[i], shares[i]);
        }

        private static void AddXp(List<TrackXp> list, int key, int xp)
        {
            if (xp <= 0) return;
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].key != key) continue;
                var entry = list[i];
                entry.xp += xp;
                list[i] = entry;
                return;
            }
            list.Add(new TrackXp { key = key, xp = xp });
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
            if (profile != null)
            {
                // Version 1 had no mastery lists: they start empty. Nothing else changed.
                profile.chassisXp ??= new List<TrackXp>();
                profile.pilotXp ??= new List<TrackXp>();
                profile.version = PlayerProfile.CurrentVersion;
                return profile;
            }
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

    /// <summary>One progression track as a screen shows it: level, XP into it, XP it needs. Plain data for the UI.</summary>
    public readonly struct TrackProgress
    {
        /// <summary>"ACCOUNT", "SCOUT", "VIPER".</summary>
        public readonly string Label;
        /// <summary>From 1.</summary>
        public readonly int Level;
        public readonly int Into;
        /// <summary>0 at the highest level.</summary>
        public readonly int Needed;
        public readonly int TotalXp;

        public TrackProgress(string label, int level, int into, int needed, int totalXp)
        {
            Label = label ?? "";
            Level = level;
            Into = into;
            Needed = needed;
            TotalXp = totalXp;
        }

        public bool IsMax => Needed <= 0;
        /// <summary>0..1 into the level; 1 at the highest level.</summary>
        public float Fill => Needed > 0 ? Mathf.Clamp01((float)Into / Needed) : 1f;
        public string LevelText => $"LVL {Level}";
        public string NeedText => IsMax ? "MAX" : $"{Into:N0} / {Needed:N0} XP";

        public static TrackProgress From(string label, LevelCurve curve, int xp)
        {
            var level = curve.Level(xp, out var into, out var needed);
            return new TrackProgress(label, level, into, needed, Math.Max(0, xp));
        }
    }

    /// <summary>
    /// The player's profile: loads it, records each match (<see cref="MatchResultsModel"/> calls
    /// <see cref="RecordMatch"/> once per match) and saves it. Online, the server will write the record and this only
    /// reads it; the screens do not change.
    /// </summary>
    public sealed class ProfileService
    {
        private readonly IProfileStore _store;
        private readonly LevelCurve _account, _chassis, _pilot;
        private PlayerProfile _profile;

        /// <param name="settings">Level curves. Null = the default curves (tests).</param>
        public ProfileService(IProfileStore store, ProgressionSettings settings = null)
        {
            _store = store;
            _account = settings != null ? settings.Account : LevelCurve.Account;
            _chassis = settings != null ? settings.Chassis : LevelCurve.Chassis;
            _pilot = settings != null ? settings.Pilot : LevelCurve.Pilot;
        }

        public PlayerProfile Profile => _profile ??= _store.Load() ?? new PlayerProfile();
        public int TotalXp => Profile.totalXp;
        /// <summary>Account level.</summary>
        public int Level => _account.Level(TotalXp);

        public LevelCurve Curve(ProgressTrack track) =>
            track == ProgressTrack.Chassis ? _chassis : track == ProgressTrack.Pilot ? _pilot : _account;

        public int ChassisXp(ChassisClass chassisClass) => Profile.ChassisXpOf((int)chassisClass);
        public int PilotXp(ushort pilotId) => Profile.PilotXpOf(pilotId);
        /// <summary>Mastery level of a chassis class (from 1).</summary>
        public int ChassisLevel(ChassisClass chassisClass) => _chassis.Level(ChassisXp(chassisClass));
        /// <summary>Mastery level of a pilot (from 1).</summary>
        public int PilotLevel(ushort pilotId) => _pilot.Level(PilotXp(pilotId));

        /// <summary>The account track at <paramref name="xp"/> (the results screen counts up through it).</summary>
        public TrackProgress AccountProgress(int xp) => TrackProgress.From("ACCOUNT", _account, xp);
        public TrackProgress Account => AccountProgress(TotalXp);
        public TrackProgress ChassisProgress(ChassisClass chassisClass) =>
            TrackProgress.From(chassisClass.ToString().ToUpperInvariant(), _chassis, ChassisXp(chassisClass));
        public TrackProgress PilotProgress(ushort pilotId, string callSign) =>
            TrackProgress.From(string.IsNullOrEmpty(callSign) ? "PILOT" : callSign, _pilot, PilotXp(pilotId));

        /// <summary>Every chassis class, in enum order.</summary>
        public List<TrackProgress> ChassisTracks()
        {
            var tracks = new List<TrackProgress>();
            foreach (ChassisClass chassisClass in Enum.GetValues(typeof(ChassisClass)))
                if (chassisClass != ChassisClass.None) tracks.Add(ChassisProgress(chassisClass));
            return tracks;
        }

        /// <summary>0..1 into the current level.</summary>
        public float LevelProgress
        {
            get
            {
                _account.Level(TotalXp, out var into, out var needed);
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

    /// <summary>
    /// The profile as the unlock source: account level and the two masteries. Not yet given to the hangar (it still
    /// uses <c>AllUnlocked</c>): that change comes with the progression screens. Local data: the server must not trust it.
    /// </summary>
    public sealed class ProfileUnlocks : IPlayerUnlocks
    {
        private readonly ProfileService _profile;

        public ProfileUnlocks(ProfileService profile) => _profile = profile;

        public int Level => _profile.Level;
        /// <summary>No purchases yet: every module is owned.</summary>
        public bool Owns(ushort moduleId) => true;
        public int ChassisLevel(ChassisClass chassisClass) => _profile.ChassisLevel(chassisClass);
        public int PilotLevel(ushort pilotId) => _profile.PilotLevel(pilotId);
    }
}
