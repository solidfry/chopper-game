using Chopper.Simulation.Match;
using NUnit.Framework;

namespace Chopper.Tests
{
    public sealed class TeamRulesTests
    {
        [Test]
        public void FreeForAll_EveryoneIsAnEnemyAndNoTeamIsGiven()
        {
            Assert.That(TeamRules.AreAllies(TeamRules.None, TeamRules.None), Is.False);
            Assert.That(TeamRules.AreEnemies(TeamRules.None, TeamRules.None), Is.True);
            Assert.That(TeamRules.JoinTeam(MatchMode.FreeForAll, 0, 0), Is.EqualTo(TeamRules.None));
            Assert.That(TeamRules.BlocksDamage(TeamRules.None, TeamRules.None, self: false), Is.False);
        }

        [Test]
        public void TeamDeathmatch_SameTeamIsAllyOtherTeamIsEnemy()
        {
            Assert.That(TeamRules.AreAllies(TeamRules.A, TeamRules.A), Is.True);
            Assert.That(TeamRules.AreEnemies(TeamRules.A, TeamRules.B), Is.True);
            Assert.That(TeamRules.AreEnemies(TeamRules.A, TeamRules.None), Is.True, "No team (environment) is never an ally.");
        }

        [Test]
        public void FriendlyFire_IsBlockedButSelfDamageStays()
        {
            Assert.That(TeamRules.BlocksDamage(TeamRules.A, TeamRules.A, self: false), Is.True);
            Assert.That(TeamRules.BlocksDamage(TeamRules.A, TeamRules.A, self: true), Is.False, "Own splash and crashes still hurt.");
            Assert.That(TeamRules.BlocksDamage(TeamRules.A, TeamRules.B, self: false), Is.False);
            Assert.That(TeamRules.BlocksDamage(TeamRules.None, TeamRules.A, self: false), Is.False, "Environment damage counts.");
        }

        [Test]
        public void JoinTeam_FillsTheSmallerTeamAndATakesTies()
        {
            Assert.That(TeamRules.JoinTeam(MatchMode.TeamDeathmatch, 0, 0), Is.EqualTo(TeamRules.A));
            Assert.That(TeamRules.JoinTeam(MatchMode.TeamDeathmatch, 1, 0), Is.EqualTo(TeamRules.B));
            Assert.That(TeamRules.JoinTeam(MatchMode.TeamDeathmatch, 1, 1), Is.EqualTo(TeamRules.A));
            Assert.That(TeamRules.JoinTeam(MatchMode.TeamDeathmatch, 2, 3), Is.EqualTo(TeamRules.A));
        }

        [Test]
        public void Leader_IsTheTeamWithMoreKillsOrNoneOnATie()
        {
            Assert.That(TeamRules.Leader(5, 3), Is.EqualTo(TeamRules.A));
            Assert.That(TeamRules.Leader(3, 5), Is.EqualTo(TeamRules.B));
            Assert.That(TeamRules.Leader(4, 4), Is.EqualTo(TeamRules.None));
        }

        [Test]
        public void ScoresForTeam_OnlyInTeamModeForAnEnemyKill()
        {
            Assert.That(TeamRules.ScoresForTeam(MatchMode.TeamDeathmatch, TeamRules.A, TeamRules.B), Is.True);
            Assert.That(TeamRules.ScoresForTeam(MatchMode.TeamDeathmatch, TeamRules.A, TeamRules.A), Is.False);
            Assert.That(TeamRules.ScoresForTeam(MatchMode.FreeForAll, TeamRules.None, TeamRules.None), Is.False);
        }

        [TestCase("tdm", MatchMode.TeamDeathmatch)]
        [TestCase(" TDM ", MatchMode.TeamDeathmatch)]
        [TestCase("TeamDeathmatch", MatchMode.TeamDeathmatch)]
        [TestCase("ffa", MatchMode.FreeForAll)]
        [TestCase("", MatchMode.FreeForAll)]
        [TestCase(null, MatchMode.FreeForAll)]
        [TestCase("ctn", MatchMode.FreeForAll)]
        public void ParseMode_ReadsTheQueueOrArgumentName(string text, MatchMode expected)
        {
            Assert.That(TeamRules.ParseMode(text), Is.EqualTo(expected));
        }
    }
}
