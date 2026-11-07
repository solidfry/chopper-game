using Chopper.Presentation.Hud;
using Chopper.Simulation.Match;
using NUnit.Framework;

namespace Chopper.Tests
{
    public sealed class MatchHudModelTests
    {
        [Test]
        public void Tick_FormatsTimerAsMinutesAndSeconds()
        {
            var model = new MatchHudModel();
            model.SetMatch(MatchPhase.InProgress, timeRemaining: 125f, winnerNetworkId: 0);
            Assert.That(model.TimerText, Is.EqualTo("02:05"));

            model.Tick(0.5f);
            Assert.That(model.TimerText, Is.EqualTo("02:05"), "Rounds up, so 0:00 shows only at the end.");

            model.Tick(200f);
            Assert.That(model.TimerText, Is.EqualTo("00:00"));
        }

        [Test]
        public void ApplyScore_ShowsLocalScoreAndLeader()
        {
            var model = new MatchHudModel();
            model.SetLocalPlayer(1);
            model.ApplyScore(1, kills: 2, deaths: 1);
            model.ApplyScore(2, kills: 5, deaths: 0);

            Assert.That(model.LocalScoreText, Is.EqualTo("2 K / 1 D"));
            Assert.That(model.LeaderText, Is.EqualTo("LEAD PILOT 2 5"));

            model.RemovePlayer(2);
            Assert.That(model.LeaderText, Is.EqualTo("LEAD YOU 2"));
        }

        [Test]
        public void AddKill_NamesLocalPlayerAndCrashes()
        {
            var model = new MatchHudModel();
            model.SetLocalPlayer(1);
            model.AddKill(killerNetworkId: 1, victimNetworkId: 3);
            model.AddKill(killerNetworkId: 0, victimNetworkId: 2);

            Assert.That(model.KillFeedText, Is.EqualTo("YOU > PILOT 3\nPILOT 2 CRASHED"));
        }

        [Test]
        public void KillFeed_KeepsNewestEntriesAndExpires()
        {
            var model = new MatchHudModel();
            for (var i = 1; i <= MatchHudModel.MaxKillFeedEntries + 1; i++)
                model.AddKill(0, i);

            Assert.That(model.KillFeedText.Split('\n'), Has.Length.EqualTo(MatchHudModel.MaxKillFeedEntries));
            Assert.That(model.KillFeedText, Does.Not.Contain("PILOT 1 "));

            model.Tick(MatchHudModel.KillFeedEntrySeconds + 0.1f);
            Assert.That(model.KillFeedText, Is.Empty);
        }

        [Test]
        public void SetMatch_ShowsBannerOnlyAfterTheMatch()
        {
            var model = new MatchHudModel();
            model.SetLocalPlayer(1);

            model.SetMatch(MatchPhase.PostGame, 10f, winnerNetworkId: 1);
            Assert.That(model.BannerVisible, Is.True);
            Assert.That(model.BannerText, Is.EqualTo("VICTORY"));

            model.SetMatch(MatchPhase.PostGame, 10f, winnerNetworkId: 0);
            Assert.That(model.BannerText, Is.EqualTo("DRAW"));

            model.SetMatch(MatchPhase.InProgress, 600f, winnerNetworkId: 0);
            Assert.That(model.BannerVisible, Is.False);
        }

        [Test]
        public void Scoreboard_SortsByKillsThenDeaths_AndSharesTiedPlaces()
        {
            var model = new MatchHudModel();
            model.SetLocalPlayer(2);
            model.ApplyScore(1, kills: 3, deaths: 2);
            model.ApplyScore(2, kills: 5, deaths: 1);
            model.ApplyScore(3, kills: 3, deaths: 2);
            model.ApplyScore(4, kills: 3, deaths: 0);
            model.SetCallSign(3, "VIPER");
            model.SetChassis(3, "GUNSHIP");

            var rows = model.Scoreboard;
            Assert.That(rows, Has.Count.EqualTo(4));
            Assert.That(rows[0].NetworkId, Is.EqualTo(2));
            Assert.That(rows[0].IsLocal, Is.True);
            Assert.That(rows[0].Name, Is.EqualTo("PILOT 2"), "the own row keeps its name; the view marks it");
            Assert.That(rows[1].NetworkId, Is.EqualTo(4), "same kills: fewer deaths first");
            Assert.That(rows[2].NetworkId, Is.EqualTo(1), "a full tie: lowest id first");
            Assert.That(rows[2].Place, Is.EqualTo(3));
            Assert.That(rows[3].Place, Is.EqualTo(3), "a full tie shares the place");
            Assert.That(rows[3].Name, Is.EqualTo("VIPER 3"));
            Assert.That(rows[3].Chassis, Is.EqualTo("GUNSHIP"));
            Assert.That(model.ScoreboardTitle, Is.EqualTo("FREE FOR ALL // 4 PILOTS"));

            model.RemovePlayer(4);
            Assert.That(model.Scoreboard, Has.Count.EqualTo(3));
        }

        [Test]
        public void Scoreboard_ShowsWhileHeld_AndOnTheResults()
        {
            var model = new MatchHudModel();
            var changes = 0;
            model.ScoreboardChanged += () => changes++;
            Assert.That(model.ScoreboardVisible, Is.False);

            model.SetScoreboardHeld(true);
            Assert.That(model.ScoreboardVisible, Is.True);
            model.SetScoreboardHeld(false);
            Assert.That(model.ScoreboardVisible, Is.False);

            model.SetMatch(MatchPhase.PostGame, 10f, winnerNetworkId: 0);
            Assert.That(model.ScoreboardVisible, Is.True, "always on the results");
            model.SetMatch(MatchPhase.InProgress, 600f, winnerNetworkId: 0);
            Assert.That(model.ScoreboardVisible, Is.False);

            model.ApplyScore(1, 1, 0);
            Assert.That(changes, Is.GreaterThan(0));
        }
    }
}
