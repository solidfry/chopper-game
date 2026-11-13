using System.Collections.Generic;
using Chopper.Presentation.Hangar;
using Chopper.Presentation.Profile;
using Chopper.Presentation.Results;
using Chopper.Simulation.Loadout;
using Chopper.Simulation.Match;
using Chopper.Simulation.Progression;
using NUnit.Framework;

namespace Chopper.Tests
{
    /// <summary>The three progression tracks (plan section 6a-1): level curves, the XP split, the profile and the locks.</summary>
    public sealed class ProgressionTests
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

        private static MatchOutcome Match(int xp, params FlightShare[] flown) =>
            new(new PlayerMatchStats { Kills = 1 }, new List<Award>(), won: false, draw: false, place: 2, players: 4, xp: xp, score: 100,
                pilot: "VIPER", flown: flown);

        [Test]
        public void LevelCurve_CountsLevelsFromOne()
        {
            var curve = new LevelCurve(1000, 250, 0);
            Assert.That(curve.Level(0, out var into, out var needed), Is.EqualTo(1));
            Assert.That((into, needed), Is.EqualTo((0, 1000)));
            Assert.That(curve.Level(1000, out into, out needed), Is.EqualTo(2));
            Assert.That((into, needed), Is.EqualTo((0, 1250)));
            Assert.That(curve.Level(2300, out into, out _), Is.EqualTo(3));
            Assert.That(into, Is.EqualTo(50));
            Assert.That(curve.Level(-5), Is.EqualTo(1));
        }

        [Test]
        public void LevelCurve_MatchesTheOldAccountLevels()
        {
            var curve = new LevelCurve(PilotLevels.FirstLevelXp, PilotLevels.StepXp, 0);
            foreach (var xp in new[] { 0, 999, 1000, 2249, 2250, 40000 })
                Assert.That(curve.Level(xp), Is.EqualTo(PilotLevels.Level(xp, out _, out _)), $"XP {xp}");
        }

        [Test]
        public void LevelCurve_StopsAtTheCap()
        {
            var curve = new LevelCurve(100, 0, 3);
            Assert.That(curve.Level(199), Is.EqualTo(2));
            Assert.That(curve.Level(200, out var into, out var needed), Is.EqualTo(3));
            Assert.That((into, needed), Is.EqualTo((0, 0)), "At the cap there is no next level.");
            Assert.That(curve.Level(1000000), Is.EqualTo(3));
        }

        [Test]
        public void LevelCurve_BadNumbersDoNotLoop()
        {
            Assert.That(new LevelCurve(0, 0, 0).Level(500), Is.EqualTo(1));
        }

        [Test]
        public void Split_AddsUpToTheXp()
        {
            var shares = new List<int>();
            MasterySplit.Split(1000, new[] { 10f, 20f, 30f }, shares);
            Assert.That(shares, Has.Count.EqualTo(3));
            Assert.That(shares[0] + shares[1] + shares[2], Is.EqualTo(1000));
            Assert.That(shares[0], Is.EqualTo(166));
            Assert.That(shares[1], Is.EqualTo(333));
            Assert.That(shares[2], Is.EqualTo(501), "The longest time gets the XP left after rounding down.");
        }

        [Test]
        public void Split_GivesNothingWithoutTime()
        {
            var shares = new List<int>();
            MasterySplit.Split(1000, new[] { 0f, -3f }, shares);
            Assert.That(shares, Is.EqualTo(new[] { 0, 0 }));
            MasterySplit.Split(0, new[] { 5f }, shares);
            Assert.That(shares, Is.EqualTo(new[] { 0 }));
            MasterySplit.Split(700, new[] { 0f, 5f }, shares);
            Assert.That(shares, Is.EqualTo(new[] { 0, 700 }));
        }

        [Test]
        public void Record_SplitsMasteryByFlightTime()
        {
            var profile = new PlayerProfile();
            ProfileRules.Record(profile, Match(1000,
                new FlightShare(ChassisClass.Scout, 3, 60f),
                new FlightShare(ChassisClass.Gunship, 3, 30f),
                new FlightShare(ChassisClass.Scout, 5, 30f)), 1);

            Assert.That(profile.totalXp, Is.EqualTo(1000), "The account gets all of it.");
            Assert.That(profile.ChassisXpOf((int)ChassisClass.Scout), Is.EqualTo(750));
            Assert.That(profile.ChassisXpOf((int)ChassisClass.Gunship), Is.EqualTo(250));
            Assert.That(profile.PilotXpOf(3), Is.EqualTo(750));
            Assert.That(profile.PilotXpOf(5), Is.EqualTo(250));

            ProfileRules.Record(profile, Match(400, new FlightShare(ChassisClass.Scout, 5, 10f)), 2);
            Assert.That(profile.ChassisXpOf((int)ChassisClass.Scout), Is.EqualTo(1150));
            Assert.That(profile.PilotXpOf(5), Is.EqualTo(650));
            Assert.That(profile.chassisXp, Has.Count.EqualTo(2), "One entry per class.");
        }

        [Test]
        public void Record_SkipsNoClassAndNoPilot_AndWorksWithoutFlightData()
        {
            var profile = new PlayerProfile();
            ProfileRules.Record(profile, Match(500, new FlightShare(ChassisClass.None, 0, 60f)), 1);
            ProfileRules.Record(profile, new MatchOutcome(new PlayerMatchStats(), new List<Award>(), false, false, 2, 4, 500, 0, "VIPER"), 2);

            Assert.That(profile.totalXp, Is.EqualTo(1000));
            Assert.That(profile.chassisXp, Is.Empty);
            Assert.That(profile.pilotXp, Is.Empty);
        }

        [Test]
        public void Service_WorksOutLevelsFromXp()
        {
            var service = new ProfileService(new PrefsProfileStore(new MemoryPrefs()));
            Assert.That(service.ChassisLevel(ChassisClass.Scout), Is.EqualTo(1));

            // Default chassis and pilot curve: 800, then +200 per level.
            service.RecordMatch(Match(1800, new FlightShare(ChassisClass.Scout, 7, 1f)));
            Assert.That(service.Level, Is.EqualTo(2));
            Assert.That(service.ChassisLevel(ChassisClass.Scout), Is.EqualTo(3));
            Assert.That(service.ChassisLevel(ChassisClass.Lifter), Is.EqualTo(1));
            Assert.That(service.PilotLevel(7), Is.EqualTo(3));
            Assert.That(service.PilotLevel(8), Is.EqualTo(1));

            IPlayerUnlocks unlocks = new ProfileUnlocks(service);
            Assert.That(unlocks.Level, Is.EqualTo(2));
            Assert.That(unlocks.ChassisLevel(ChassisClass.Scout), Is.EqualTo(3));
            Assert.That(unlocks.PilotLevel(7), Is.EqualTo(3));
        }

        [Test]
        public void Store_ReadsAVersionOneSave_AndKeepsMastery()
        {
            var prefs = new MemoryPrefs();
            prefs.Strings["Chopper.Profile"] = "{\"version\":1,\"totalXp\":2500,\"matches\":4,\"kills\":9}";
            var store = new PrefsProfileStore(prefs);

            var old = store.Load();
            Assert.That(old.totalXp, Is.EqualTo(2500));
            Assert.That(old.kills, Is.EqualTo(9));
            Assert.That(old.version, Is.EqualTo(PlayerProfile.CurrentVersion));
            Assert.That(old.chassisXp, Is.Empty);
            Assert.That(old.pilotXp, Is.Empty);

            ProfileRules.Record(old, Match(600, new FlightShare(ChassisClass.Assault, 2, 1f)), 1);
            store.Save(old);
            var again = store.Load();
            Assert.That(again.ChassisXpOf((int)ChassisClass.Assault), Is.EqualTo(600));
            Assert.That(again.PilotXpOf(2), Is.EqualTo(600));
        }

        [Test]
        public void MatchXp_UsesTheRates()
        {
            var lines = new List<XpLine>();
            var rates = new XpRates { MatchComplete = 10, Victory = 20, PerKill = 5, PerDamage = 0.5f };
            var total = MatchXp.Lines(new PlayerMatchStats { Kills = 3, DamageDealt = 100f }, new List<Award>(), won: true, lines, rates);
            Assert.That(total, Is.EqualTo(10 + 20 + 15 + 50));

            total = MatchXp.Lines(new PlayerMatchStats { Kills = 3 }, new List<Award>(), won: true, lines, default);
            Assert.That(total, Is.Zero);
            Assert.That(lines, Is.Empty, "A rate of 0 shows no line.");
        }

        [Test]
        public void ResultsModel_GivesMasteryToTheLocalChassisAndPilot()
        {
            var hud = new Chopper.Presentation.Hud.MatchHudModel();
            hud.SetLocalPlayer(1);
            hud.ApplyScore(1, 2, 0);
            hud.ApplyScore(2, 0, 2);
            var service = new ProfileService(new PrefsProfileStore(new MemoryPrefs()));
            var model = new MatchResultsModel(hud, service, null, () => new FlightShare(ChassisClass.Gunship, 4, 1f));

            model.SetMatch(MatchPhase.PostGame, 1);
            model.ApplyStats(1, new PlayerMatchStats { Kills = 2 });
            model.ApplyStats(2, new PlayerMatchStats { Deaths = 2 });
            model.Tick(MatchResultsModel.SettleSeconds + 0.01f);

            Assert.That(model.XpGained, Is.GreaterThan(0));
            Assert.That(service.ChassisXp(ChassisClass.Gunship), Is.EqualTo(model.XpGained));
            Assert.That(service.PilotXp(4), Is.EqualTo(model.XpGained));
            Assert.That(service.TotalXp, Is.EqualTo(model.XpGained));
        }
    }
}
