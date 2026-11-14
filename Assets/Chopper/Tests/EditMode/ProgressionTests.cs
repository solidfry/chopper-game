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

            Assert.That(model.Mastery, Has.Count.EqualTo(2), "The chassis class, then the pilot.");
            Assert.That(model.Mastery[0].Label, Is.EqualTo("GUNSHIP"));
            Assert.That(model.Mastery[0].Gained, Is.EqualTo(model.XpGained));
            Assert.That(model.Mastery[0].Before.TotalXp, Is.Zero);
            Assert.That(model.Mastery[1].Gained, Is.EqualTo(model.XpGained));

            model.SetMatch(MatchPhase.InProgress, 0);
            Assert.That(model.Mastery, Is.Empty, "A new match clears the rows.");
        }

        [Test]
        public void ResultsModel_HasNoMasteryRowsWithoutFlightData()
        {
            var hud = new Chopper.Presentation.Hud.MatchHudModel();
            hud.SetLocalPlayer(1);
            hud.ApplyScore(1, 1, 0);
            var model = new MatchResultsModel(hud, new ProfileService(new PrefsProfileStore(new MemoryPrefs())));
            model.SetMatch(MatchPhase.PostGame, 1);
            model.ApplyStats(1, new PlayerMatchStats { Kills = 1 });
            model.Tick(MatchResultsModel.SettleSeconds + 0.01f);
            Assert.That(model.Mastery, Is.Empty);
        }

        private sealed class GateCatalog : ILoadoutCatalog
        {
            public const ushort Scout = 1, Assault = 2, Gun = 101, AgileRotor = 152, RacingRotor = 153;
            private static readonly SlotType[] Slots = { SlotType.Nose, SlotType.Rotor };
            private static ModuleStats Light => new()
                { Weight = 5f, LiftMultiplier = 1f, TurnRateMultiplier = 1f, BoostRechargeMultiplier = 1f, SpeedMultiplier = 1f, BoostPowerMultiplier = 1f };

            public bool TryGetChassis(ushort id, out ChassisSpec chassis)
            {
                chassis = id == Scout ? new ChassisSpec(Scout, "Scout", 70f, 60f, Slots, 0, ChassisClass.Scout)
                    : id == Assault ? new ChassisSpec(Assault, "Assault", 150f, 130f, Slots, 4, ChassisClass.Assault)
                    : null;
                return chassis != null;
            }

            public bool TryGetModule(ushort id, out ModuleSpec module)
            {
                module = id == Gun ? new ModuleSpec(Gun, "Gun", SlotType.Nose, Light)
                    : id == AgileRotor ? new ModuleSpec(AgileRotor, "Agile", SlotType.Rotor, Light, unlockLevel: 4)
                    : id == RacingRotor ? new ModuleSpec(RacingRotor, "Racing", SlotType.Rotor, Light, masteryLevel: 5, parentId: AgileRotor)
                    : null;
                return module != null;
            }
        }

        private static LoadoutCode Code(ushort chassis, ushort nose, ushort rotor, ushort pilot = 0)
        {
            var code = new LoadoutCode { ChassisId = chassis, PilotId = pilot };
            code.SetModule(0, nose);
            code.SetModule(1, rotor);
            return code;
        }

        [Test]
        public void Gate_LeavesAnUnlockedLoadoutAlone()
        {
            var code = Code(GateCatalog.Assault, GateCatalog.Gun, GateCatalog.RacingRotor, pilot: 3);
            ushort skin = 7;
            var change = LoadoutGate.Apply(ref code, ref skin, new GateCatalog(), new ProgressUnlocks(10, 5, 1), pilotLevelNeeded: 10,
                skinLevelNeeded: 10, Code(GateCatalog.Assault, GateCatalog.Gun, 0));
            Assert.That(change, Is.EqualTo(GateChange.None));
            Assert.That(code.GetModule(1), Is.EqualTo(GateCatalog.RacingRotor));
            Assert.That(code.PilotId, Is.EqualTo(3));
            Assert.That(skin, Is.EqualTo(7));
        }

        [Test]
        public void Gate_GivesTheDefaultChopperForALockedChassis()
        {
            var code = Code(GateCatalog.Assault, GateCatalog.Gun, 0, pilot: 3);
            ushort skin = 0;
            var change = LoadoutGate.Apply(ref code, ref skin, new GateCatalog(), new ProgressUnlocks(3, 1, 1), 0, 0, default);
            Assert.That(change, Is.EqualTo(GateChange.Chassis));
            Assert.That(code.IsEmpty, Is.True);
        }

        [Test]
        public void Gate_GivesTheChassisDefaultForALockedModule_AndKeepsThePilot()
        {
            var fallback = Code(GateCatalog.Scout, GateCatalog.Gun, 0);
            foreach (var (rotor, unlocks) in new[]
                     {
                         (GateCatalog.AgileRotor, new ProgressUnlocks(3, 9, 1)),
                         (GateCatalog.RacingRotor, new ProgressUnlocks(9, 4, 1)),
                     })
            {
                var code = Code(GateCatalog.Scout, GateCatalog.Gun, rotor, pilot: 3);
                ushort skin = 0;
                var change = LoadoutGate.Apply(ref code, ref skin, new GateCatalog(), unlocks, 0, 0, fallback);
                Assert.That(change, Is.EqualTo(GateChange.Modules), $"Rotor {rotor}");
                Assert.That(code.GetModule(0), Is.EqualTo(GateCatalog.Gun));
                Assert.That(code.GetModule(1), Is.Zero);
                Assert.That(code.PilotId, Is.EqualTo(3));
            }
        }

        [Test]
        public void Gate_TakesALockedPilotAndSkinAway()
        {
            var code = Code(GateCatalog.Scout, GateCatalog.Gun, 0, pilot: 3);
            ushort skin = 7;
            var change = LoadoutGate.Apply(ref code, ref skin, new GateCatalog(), new ProgressUnlocks(2, 1, 1), pilotLevelNeeded: 5,
                skinLevelNeeded: 3, default);
            Assert.That(change, Is.EqualTo(GateChange.Pilot | GateChange.Skin));
            Assert.That(code.PilotId, Is.Zero);
            Assert.That(code.ChassisId, Is.EqualTo(GateCatalog.Scout), "The chassis and modules stay.");
            Assert.That(skin, Is.Zero);

            // No loadout chosen: only the skin can be checked.
            var empty = default(LoadoutCode);
            skin = 7;
            Assert.That(LoadoutGate.Apply(ref empty, ref skin, new GateCatalog(), new ProgressUnlocks(9, 1, 1), 0, 3, default),
                Is.EqualTo(GateChange.None));
            Assert.That(skin, Is.EqualTo(7));
        }

        [Test]
        public void ProgressUnlocks_WorksOutLevelsFromTheRules()
        {
            var rules = new ProgressionRules { Account = LevelCurve.Account, Chassis = LevelCurve.Chassis, Pilot = LevelCurve.Pilot };
            IPlayerUnlocks unlocks = new ProgressUnlocks(new PlayerProgress { AccountXp = 1000, ChassisXp = 1800, PilotXp = 0 }, rules);
            Assert.That(unlocks.Level, Is.EqualTo(2));
            Assert.That(unlocks.ChassisLevel(ChassisClass.Scout), Is.EqualTo(3));
            Assert.That(unlocks.PilotLevel(3), Is.EqualTo(1));
        }

        [Test]
        public void MatchXp_GivesLessForBotKills()
        {
            var lines = new List<XpLine>();
            var rates = new XpRates { PerKill = 100, BotKillFactor = 0.5f };
            var total = MatchXp.Lines(new PlayerMatchStats { Kills = 5, BotKills = 2 }, new List<Award>(), won: false, lines, rates);
            Assert.That(total, Is.EqualTo(300 + 100));
            Assert.That(lines, Has.Count.EqualTo(2));
            Assert.That(lines[1].Label, Is.EqualTo("BOT KILLS x2"));

            total = MatchXp.Lines(new PlayerMatchStats { Kills = 2, BotKills = 2 }, new List<Award>(), won: false, lines,
                new XpRates { PerKill = 100, BotKillFactor = 0f });
            Assert.That(total, Is.Zero, "A factor of 0: bot kills give nothing.");
        }

        [Test]
        public void Grant_PlacesByKillsThenFewestDeaths()
        {
            var all = new Dictionary<int, PlayerMatchStats>
            {
                [1] = new() { Kills = 5, Deaths = 2 },
                [2] = new() { Kills = 5, Deaths = 1 },
                [3] = new() { Kills = 2, Deaths = 0 },
                [4] = new() { Kills = 2, Deaths = 0 },
            };
            Assert.That(MatchGrant.Place(2, all), Is.EqualTo(1));
            Assert.That(MatchGrant.Place(1, all), Is.EqualTo(2));
            Assert.That(MatchGrant.Place(3, all), Is.EqualTo(3));
            Assert.That(MatchGrant.Place(4, all), Is.EqualTo(3), "The same kills and deaths share a place.");
            Assert.That(MatchGrant.Place(9, all), Is.EqualTo(4), "Not known: last.");
        }

        [Test]
        public void Grant_GivesTheSameXpAsTheResultsScreen_AndRecordsMastery()
        {
            var all = new Dictionary<int, PlayerMatchStats>
            {
                [1] = new() { Kills = 3, BotKills = 1, DamageDealt = 500f },
                [2] = new() { Kills = 1, Deaths = 3 },
            };
            var rates = XpRates.Default;
            var outcome = MatchGrant.Outcome(1, all, winnerId: 1, rates, "VIPER", new FlightShare(ChassisClass.Scout, 3, 1f));

            var awards = new List<Award>();
            MatchAwards.For(1, all, awards);
            var expected = MatchXp.Lines(all[1], awards, won: true, new List<XpLine>(), rates);
            Assert.That(outcome.Xp, Is.EqualTo(expected));
            Assert.That(outcome.Won, Is.True);
            Assert.That(outcome.Draw, Is.False);
            Assert.That(outcome.Place, Is.EqualTo(1));
            Assert.That(outcome.Players, Is.EqualTo(2));

            var profile = new PlayerProfile();
            ProfileRules.Record(profile, outcome, 1);
            Assert.That(profile.totalXp, Is.EqualTo(expected));
            Assert.That(profile.ChassisXpOf((int)ChassisClass.Scout), Is.EqualTo(expected));
            Assert.That(profile.PilotXpOf(3), Is.EqualTo(expected));
            Assert.That(profile.recent[0].pilot, Is.EqualTo("VIPER"));

            var loser = MatchGrant.Outcome(2, all, winnerId: 1, rates, "", default);
            Assert.That(loser.Won, Is.False);
            Assert.That(loser.Place, Is.EqualTo(2));
            Assert.That(MatchGrant.Outcome(2, all, winnerId: 0, rates, "", default).Draw, Is.True);
        }

        [Test]
        public void TrackProgress_ShowsLevelFillAndTheCap()
        {
            var half = TrackProgress.From("SCOUT", new LevelCurve(1000, 0, 3), 1500);
            Assert.That(half.Level, Is.EqualTo(2));
            Assert.That(half.Fill, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(half.IsMax, Is.False);
            Assert.That(half.LevelText, Is.EqualTo("LVL 2"));

            var top = TrackProgress.From("SCOUT", new LevelCurve(1000, 0, 3), 9000);
            Assert.That(top.Level, Is.EqualTo(3));
            Assert.That(top.IsMax, Is.True);
            Assert.That(top.Fill, Is.EqualTo(1f));
            Assert.That(top.NeedText, Is.EqualTo("MAX"));
            Assert.That(top.TotalXp, Is.EqualTo(9000));
        }

        [Test]
        public void Service_ListsEveryChassisClass()
        {
            var service = new ProfileService(new PrefsProfileStore(new MemoryPrefs()));
            service.RecordMatch(Match(900, new FlightShare(ChassisClass.Lifter, 1, 1f)));
            var tracks = service.ChassisTracks();
            Assert.That(tracks, Has.Count.EqualTo(4));
            Assert.That(tracks[0].Label, Is.EqualTo("SCOUT"));
            Assert.That(tracks[0].Level, Is.EqualTo(1));
            Assert.That(tracks[3].Label, Is.EqualTo("LIFTER"));
            Assert.That(tracks[3].Level, Is.EqualTo(2));
            Assert.That(service.Account.Label, Is.EqualTo("ACCOUNT"));
        }
    }
}
