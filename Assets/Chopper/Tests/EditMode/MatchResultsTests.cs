using System.Collections.Generic;
using System.Linq;
using Chopper.Presentation.Hangar;
using Chopper.Presentation.Hud;
using Chopper.Presentation.Profile;
using Chopper.Presentation.Results;
using Chopper.Simulation.Combat;
using Chopper.Simulation.Match;
using Chopper.Simulation.Session;
using NUnit.Framework;

namespace Chopper.Tests
{
    /// <summary>Match stats (server rules), awards, XP and levels, and the results model.</summary>
    public sealed class MatchResultsTests
    {
        private sealed class MemoryPrefs : IHangarPrefs
        {
            private readonly Dictionary<string, string> _strings = new();
            private readonly Dictionary<string, int> _ints = new();
            public string GetString(string key, string fallback) => _strings.TryGetValue(key, out var v) ? v : fallback;
            public int GetInt(string key, int fallback) => _ints.TryGetValue(key, out var v) ? v : fallback;
            public void SetString(string key, string value) => _strings[key] = value;
            public void SetInt(string key, int value) => _ints[key] = value;
            public void Save() { }
        }

        private static MatchLogEntry Entry(MatchLogKind kind, int actor, int target, DamageKind damage = DamageKind.None,
            float amount = 0f, double time = 0, float distance = -1f) => new()
        {
            Kind = kind,
            Actor = actor,
            Target = target,
            Damage = damage,
            Amount = amount,
            Time = time,
            Distance = distance,
        };

        [Test]
        public void Stats_CountHitsKillsStreaksAndFirstBlood()
        {
            PlayerStats a = default, b = default;
            var firstBlood = false;
            MatchStatsRules.Apply(Entry(MatchLogKind.Shot, 1, 0), ref a, true, ref b, false, ref firstBlood);
            MatchStatsRules.Apply(Entry(MatchLogKind.Hit, 1, 2, DamageKind.Bullet, 30f), ref a, true, ref b, true, ref firstBlood);
            MatchStatsRules.Apply(Entry(MatchLogKind.Kill, 1, 2, DamageKind.Bullet, distance: 420f), ref a, true, ref b, true, ref firstBlood);
            MatchStatsRules.Apply(Entry(MatchLogKind.Kill, 1, 2, DamageKind.Bullet, distance: 100f), ref a, true, ref b, true, ref firstBlood);

            Assert.That(a.Value.Shots, Is.EqualTo(1));
            Assert.That(a.Value.Hits, Is.EqualTo(1));
            Assert.That(a.Value.DamageDealt, Is.EqualTo(30f));
            Assert.That(b.Value.DamageTaken, Is.EqualTo(30f));
            Assert.That(a.Value.Kills, Is.EqualTo(2));
            Assert.That(a.Value.BestStreak, Is.EqualTo(2));
            Assert.That(a.Value.LongestKill, Is.EqualTo(420f));
            Assert.That(a.Value.FirstBlood, Is.True);
            Assert.That(b.Value.Deaths, Is.EqualTo(2));
        }

        [Test]
        public void Stats_CrashDeathIsACrash_RamHitsInOneContactAreOneRam()
        {
            PlayerStats a = default, b = default;
            var firstBlood = false;
            MatchStatsRules.Apply(Entry(MatchLogKind.Kill, 0, 2, DamageKind.Impact), ref a, false, ref b, true, ref firstBlood);
            Assert.That(b.Value.Crashes, Is.EqualTo(1));
            Assert.That(firstBlood, Is.False, "A crash is not first blood.");

            MatchStatsRules.Apply(Entry(MatchLogKind.Hit, 1, 2, DamageKind.Impact, 10f, time: 5.0), ref a, true, ref b, true, ref firstBlood);
            MatchStatsRules.Apply(Entry(MatchLogKind.Hit, 1, 2, DamageKind.Impact, 10f, time: 5.1), ref a, true, ref b, true, ref firstBlood);
            MatchStatsRules.Apply(Entry(MatchLogKind.Hit, 1, 2, DamageKind.Impact, 10f, time: 7.0), ref a, true, ref b, true, ref firstBlood);
            Assert.That(a.Value.Rams, Is.EqualTo(2));
            Assert.That(a.Value.Hits, Is.EqualTo(0), "Rams are not gun hits.");
        }

        private static Dictionary<int, PlayerMatchStats> Match(params (int Id, PlayerMatchStats Stats)[] players) =>
            players.ToDictionary(p => p.Id, p => p.Stats);

        private static List<string> Titles(int id, Dictionary<int, PlayerMatchStats> all)
        {
            var list = new List<Award>();
            MatchAwards.For(id, all, list);
            return list.Select(a => a.Title).ToList();
        }

        [Test]
        public void Awards_FeatsJokesAndShame()
        {
            var all = Match(
                (1, new PlayerMatchStats { Kills = 6, Deaths = 0, BestStreak = 6, FirstBlood = true, Shots = 40, Hits = 30 }),
                (2, new PlayerMatchStats { Kills = 0, Deaths = 6, Crashes = 3, Shots = 100, Hits = 4 }));

            var ace = Titles(1, all);
            Assert.That(ace, Does.Contain("FLAWLESS"));
            Assert.That(ace, Does.Contain("ACE"));
            Assert.That(ace.Count, Is.LessThanOrEqualTo(MatchAwards.MaxGlory + MatchAwards.MaxFunny));

            var loser = Titles(2, all);
            Assert.That(loser, Has.Count.EqualTo(1), "One joke at most; no feats.");
            Assert.That(loser[0], Is.EqualTo("PIÑATA"));
        }

        [Test]
        public void Awards_NobodyGetsNothing_AndAloneHasNoMostAwards()
        {
            var alone = Match((1, new PlayerMatchStats { Kills = 1, Deaths = 5 }));
            var titles = Titles(1, alone);
            Assert.That(titles, Does.Not.Contain("TOP GUN"));
            Assert.That(titles, Does.Not.Contain("PIÑATA"));

            var quiet = Match((1, new PlayerMatchStats { Shots = 20, Hits = 5, Kills = 1, Deaths = 1 }),
                (2, new PlayerMatchStats { Shots = 20, Hits = 5, Kills = 1, Deaths = 1 }));
            Assert.That(Titles(1, quiet), Is.EqualTo(new[] { "PARTICIPATION TROPHY" }));
        }

        [Test]
        public void Xp_LinesAddUp_AndLevelsCostMore()
        {
            var lines = new List<XpLine>();
            var awards = new List<Award> { new("ACE", "", AwardTone.Glory, 250, 0) };
            var total = MatchXp.Lines(new PlayerMatchStats { Kills = 3, DamageDealt = 500f }, awards, won: true, lines);
            Assert.That(total, Is.EqualTo(MatchXp.MatchComplete + MatchXp.Victory + 300 + 100 + 250));
            Assert.That(lines.Sum(l => l.Amount), Is.EqualTo(total));

            Assert.That(PilotLevels.Level(0, out var into, out var needed), Is.EqualTo(1));
            Assert.That((into, needed), Is.EqualTo((0, 1000)));
            Assert.That(PilotLevels.Level(1000, out into, out needed), Is.EqualTo(2));
            Assert.That((into, needed), Is.EqualTo((0, 1250)));
            Assert.That(PilotLevels.Level(2300, out into, out _), Is.EqualTo(3));
            Assert.That(into, Is.EqualTo(50));
        }

        [Test]
        public void Model_BuildsAfterSettle_AddsXpOnce_ClearsOnNewMatch()
        {
            var hud = new MatchHudModel();
            hud.SetLocalPlayer(1);
            hud.ApplyScore(1, 2, 0);
            hud.ApplyScore(2, 0, 2);
            var progress = new ProfileService(new PrefsProfileStore(new MemoryPrefs()));
            var model = new MatchResultsModel(hud, progress);

            model.SetMatch(MatchPhase.PostGame, 1);
            model.ApplyStats(1, new PlayerMatchStats { Kills = 2 });
            model.ApplyStats(2, new PlayerMatchStats { Deaths = 2 });
            Assert.That(model.Visible, Is.False, "Waits for the stats to settle.");

            model.Tick(MatchResultsModel.SettleSeconds + 0.01f);
            Assert.That(model.Visible, Is.True);
            Assert.That(model.Won, Is.True);
            Assert.That(model.Local.NetworkId, Is.EqualTo(1));
            Assert.That(model.Players, Has.Count.EqualTo(2));
            var gained = model.XpGained;
            Assert.That(progress.TotalXp, Is.EqualTo(gained));

            model.ApplyStats(2, new PlayerMatchStats { Deaths = 2 });
            model.Tick(1f);
            Assert.That(progress.TotalXp, Is.EqualTo(gained), "A late message does not add XP again.");

            Assert.That(progress.Profile.matches, Is.EqualTo(1));
            Assert.That(progress.Profile.kills, Is.EqualTo(2));

            model.Dismiss();
            Assert.That(model.Visible, Is.False);
            model.SetMatch(MatchPhase.InProgress, 0);
            Assert.That(model.Ready, Is.False);
        }
    }
}
