using Chopper.Simulation.Match;
using NUnit.Framework;
using Unity.Collections;

namespace Chopper.Tests
{
    public sealed class TeamRulesTests
    {
        [Test]
        public void FreeForAll_EveryoneIsAnEnemyAndNoTeamIsGiven()
        {
            Assert.That(TeamRules.AreAllies(TeamRules.None, TeamRules.None), Is.False);
            Assert.That(TeamRules.AreEnemies(TeamRules.None, TeamRules.None), Is.True);
            Assert.That(TeamRules.JoinTeam(MatchMode.FreeForAll, List(0, 0)), Is.EqualTo(TeamRules.None));
            Assert.That(TeamRules.BlocksDamage(TeamRules.None, TeamRules.None, self: false), Is.False);
        }

        [Test]
        public void TeamDeathmatch_SameTeamIsAllyOtherTeamIsEnemy()
        {
            Assert.That(TeamRules.AreAllies(1, 1), Is.True);
            Assert.That(TeamRules.AreEnemies(1, 2), Is.True);
            Assert.That(TeamRules.AreEnemies(3, 5), Is.True, "Any number of teams.");
            Assert.That(TeamRules.AreEnemies(1, TeamRules.None), Is.True, "No team (environment) is never an ally.");
        }

        [Test]
        public void FriendlyFire_IsBlockedButSelfDamageStays()
        {
            Assert.That(TeamRules.BlocksDamage(1, 1, self: false), Is.True);
            Assert.That(TeamRules.BlocksDamage(1, 1, self: true), Is.False, "Own splash and crashes still hurt.");
            Assert.That(TeamRules.BlocksDamage(1, 2, self: false), Is.False);
            Assert.That(TeamRules.BlocksDamage(TeamRules.None, 1, self: false), Is.False, "Environment damage counts.");
        }

        [Test]
        public void JoinTeam_FillsTheSmallestTeamAndTheLowestNumberTakesTies()
        {
            Assert.That(TeamRules.JoinTeam(MatchMode.TeamDeathmatch, List(0, 0)), Is.EqualTo(1));
            Assert.That(TeamRules.JoinTeam(MatchMode.TeamDeathmatch, List(1, 0)), Is.EqualTo(2));
            Assert.That(TeamRules.JoinTeam(MatchMode.TeamDeathmatch, List(1, 1)), Is.EqualTo(1));
            Assert.That(TeamRules.JoinTeam(MatchMode.TeamDeathmatch, List(2, 3)), Is.EqualTo(1));
            Assert.That(TeamRules.JoinTeam(MatchMode.TeamDeathmatch, List(2, 2, 1, 2)), Is.EqualTo(3), "Four teams.");
        }

        [Test]
        public void Leader_IsTheTeamWithMoreKillsOrNoneOnATie()
        {
            Assert.That(TeamRules.Leader(List(5, 3)), Is.EqualTo(1));
            Assert.That(TeamRules.Leader(List(3, 5)), Is.EqualTo(2));
            Assert.That(TeamRules.Leader(List(4, 4)), Is.EqualTo(TeamRules.None));
            Assert.That(TeamRules.Leader(List(4, 4, 6)), Is.EqualTo(3), "A shared lower score is no draw.");
            Assert.That(TeamRules.Leader(List(6, 2, 6)), Is.EqualTo(TeamRules.None));
            Assert.That(TeamRules.Leader(List()), Is.EqualTo(TeamRules.None));
        }

        [Test]
        public void ScoresForTeam_OnlyInTeamModeForAnEnemyKill()
        {
            Assert.That(TeamRules.ScoresForTeam(MatchMode.TeamDeathmatch, 1, 2), Is.True);
            Assert.That(TeamRules.ScoresForTeam(MatchMode.TeamDeathmatch, 1, 1), Is.False);
            Assert.That(TeamRules.ScoresForTeam(MatchMode.FreeForAll, TeamRules.None, TeamRules.None), Is.False);
        }

        [Test]
        public void TeamCount_IsTwoToMaxTeams()
        {
            Assert.That(TeamRules.ClampTeamCount(0), Is.EqualTo(2));
            Assert.That(TeamRules.ClampTeamCount(4), Is.EqualTo(4));
            Assert.That(TeamRules.ClampTeamCount(99), Is.EqualTo(TeamRules.MaxTeams));
            Assert.That(TeamRules.Zeros(3).Length, Is.EqualTo(3));
        }

        private static FixedList64Bytes<int> List(params int[] values)
        {
            var list = new FixedList64Bytes<int>();
            foreach (var value in values) list.Add(value);
            return list;
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
