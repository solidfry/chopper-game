using System.Collections.Generic;
using Chopper.Presentation.Hangar;
using Chopper.Presentation.Profile;
using Chopper.Presentation.Results;
using Chopper.Simulation.Match;
using NUnit.Framework;

namespace Chopper.Tests
{
    /// <summary>The lifetime profile: totals, award counts, recent matches, achievements, and the save.</summary>
    public sealed class ProfileTests
    {
        private sealed class MemoryPrefs : IHangarPrefs
        {
            public readonly Dictionary<string, string> Strings = new();
            public readonly Dictionary<string, int> Ints = new();
            public string GetString(string key, string fallback) => Strings.TryGetValue(key, out var v) ? v : fallback;
            public int GetInt(string key, int fallback) => Ints.TryGetValue(key, out var v) ? v : fallback;
            public void SetString(string key, string value) => Strings[key] = value;
            public void SetInt(string key, int value) => Ints[key] = value;
            public void Save() { }
        }

        private static MatchOutcome Match(int kills, int deaths, bool won, params string[] awards)
        {
            var list = new List<Award>();
            foreach (var title in awards) list.Add(new Award(title, "", AwardTone.Glory, 50, 0));
            return new MatchOutcome(new PlayerMatchStats { Kills = kills, Deaths = deaths, Shots = 10, Hits = 4, LongestKill = 300f },
                list, won, draw: false, place: won ? 1 : 2, players: 4, xp: 500, score: kills * 100, pilot: "VIPER");
        }

        [Test]
        public void Record_AddsTotalsAwardsAndRecent()
        {
            var profile = new PlayerProfile();
            ProfileRules.Record(profile, Match(3, 1, true, "ACE", "TOP GUN"), 100);
            ProfileRules.Record(profile, Match(2, 4, false, "ACE"), 200);

            Assert.That(profile.matches, Is.EqualTo(2));
            Assert.That(profile.wins, Is.EqualTo(1));
            Assert.That(profile.kills, Is.EqualTo(5));
            Assert.That(profile.deaths, Is.EqualTo(5));
            Assert.That(profile.totalXp, Is.EqualTo(1000));
            Assert.That(profile.Accuracy, Is.EqualTo(0.4f).Within(0.001f));
            Assert.That(profile.AwardCountOf("ACE"), Is.EqualTo(2));
            Assert.That(profile.AwardCountOf("TOP GUN"), Is.EqualTo(1));
            Assert.That(profile.recent[0].unixTime, Is.EqualTo(200), "Newest first.");
            Assert.That(profile.recent[1].awards, Is.EqualTo(new[] { "ACE", "TOP GUN" }));
        }

        [Test]
        public void Record_KeepsOnlyTheLastMatches_AndUnlocksAchievementsOnce()
        {
            var profile = new PlayerProfile();
            var first = ProfileRules.Record(profile, Match(1, 0, false), 1);
            Assert.That(first, Does.Contain("first_sortie"));

            for (var i = 0; i < PlayerProfile.RecentLimit + 5; i++)
            {
                var unlocked = ProfileRules.Record(profile, Match(1, 0, false), 10 + i);
                Assert.That(unlocked, Does.Not.Contain("first_sortie"));
            }
            Assert.That(profile.recent, Has.Count.EqualTo(PlayerProfile.RecentLimit));
            Assert.That(profile.HasAchievement("first_sortie"), Is.True);
        }

        [Test]
        public void Store_RoundTripsJson_AndReadsTheOldXpTotal()
        {
            var prefs = new MemoryPrefs();
            prefs.Ints["Chopper.Profile.Xp"] = 1234;
            var store = new PrefsProfileStore(prefs);
            Assert.That(store.Load().totalXp, Is.EqualTo(1234), "The first results screen kept only an XP total.");

            var service = new ProfileService(store);
            service.RecordMatch(Match(4, 2, true, "ACE"));
            var again = new ProfileService(new PrefsProfileStore(prefs));
            Assert.That(again.Profile.kills, Is.EqualTo(4));
            Assert.That(again.Profile.totalXp, Is.EqualTo(1734));
            Assert.That(again.Profile.AwardCountOf("ACE"), Is.EqualTo(1));
            Assert.That(again.Profile.recent[0].pilot, Is.EqualTo("VIPER"));
        }

        [Test]
        public void Catalogue_HasEveryAwardOnce()
        {
            var titles = new HashSet<string>();
            foreach (var (title, _, description) in MatchAwards.Catalogue())
            {
                Assert.That(titles.Add(title), Is.True, title);
                Assert.That(description, Is.Not.Empty, title);
            }
            Assert.That(titles, Does.Contain(MatchAwards.ParticipationTitle));
        }
    }
}
