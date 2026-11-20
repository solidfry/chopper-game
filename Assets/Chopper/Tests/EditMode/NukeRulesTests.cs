using Chopper.Simulation.Match;
using NUnit.Framework;
using Unity.Collections;

namespace Chopper.Tests
{
    public sealed class NukeRulesTests
    {
        private static readonly NukeTuning Tuning = NukeTuning.Default;

        private static NukeEvent Run(ref NukeState s, byte carrier, byte zone, float seconds, bool contested = false)
        {
            var last = NukeEvent.None;
            var input = new NukeInputs { CarrierTeam = carrier, ZoneTeam = zone, ZoneContested = contested };
            for (var t = 0f; t < seconds - 1e-4f; t += 0.5f)
            {
                var e = NukeRules.Tick(ref s, input, Tuning, 0.5f);
                if (e != NukeEvent.None) last = e;
            }
            return last;
        }

        [Test]
        public void Mode_ParsesCtnAndCountsAsATeamMode()
        {
            Assert.That(TeamRules.ParseMode("ctn"), Is.EqualTo(MatchMode.CaptureTheNuke));
            Assert.That(TeamRules.ParseMode("CaptureTheNuke"), Is.EqualTo(MatchMode.CaptureTheNuke));
            Assert.That(TeamRules.IsTeamMode(MatchMode.CaptureTheNuke), Is.True);
        }

        [Test]
        public void PickUp_ThenDrop_ThenResetsToThePadAfter30s()
        {
            var s = NukeRules.AtSpawn;
            Assert.That(NukeRules.Tick(ref s, new NukeInputs { CarrierTeam = 1 }, Tuning, 0.1f), Is.EqualTo(NukeEvent.PickedUp));
            Assert.That(NukeRules.Tick(ref s, new NukeInputs(), Tuning, 0.1f), Is.EqualTo(NukeEvent.Dropped));
            Assert.That(s.Phase, Is.EqualTo(NukePhase.Dropped));
            Assert.That(Run(ref s, 0, 0, 30f), Is.EqualTo(NukeEvent.Reset));
            Assert.That(s.Phase, Is.EqualTo(NukePhase.AtSpawn));
        }

        [Test]
        public void Arm_TakesTwentySecondsInOwnZone_AndPausesWhenContested()
        {
            var s = NukeRules.AtSpawn;
            Run(ref s, 1, 1, 10f);
            Assert.That(s.ArmProgress, Is.EqualTo(0.5f).Within(0.01f));
            Run(ref s, 1, 1, 5f, contested: true);
            Assert.That(s.ArmProgress, Is.EqualTo(0.5f).Within(0.01f), "Enemies in the zone pause arming.");
            Assert.That(Run(ref s, 1, 1, 10f), Is.EqualTo(NukeEvent.Armed));
            Assert.That(s.ArmedBy, Is.EqualTo(1));
        }

        [Test]
        public void Arm_DecaysOutOfZone_AndAnotherTeamStartsFromZero()
        {
            var s = NukeRules.AtSpawn;
            Run(ref s, 1, 1, 10f);
            Run(ref s, 1, 0, 2f);
            Assert.That(s.ArmProgress, Is.LessThan(0.5f));
            Run(ref s, 2, 0, 0.5f);
            Assert.That(s.ArmProgress, Is.EqualTo(0f));
        }

        [Test]
        public void ArmedNuke_InTheEnemyZone_Detonates()
        {
            var s = NukeRules.AtSpawn;
            Run(ref s, 1, 1, 20f);
            Assert.That(Run(ref s, 1, 2, 0.5f), Is.EqualTo(NukeEvent.Detonated));
            Assert.That(s.Phase, Is.EqualTo(NukePhase.Detonated));
            Assert.That(s.DetonatedBy, Is.EqualTo(1));
            Assert.That(Run(ref s, 0, 0, 5f), Is.EqualTo(NukeEvent.None), "Nothing happens after the blast.");
        }

        [Test]
        public void UnarmedNuke_InTheEnemyZone_DoesNothing()
        {
            var s = NukeRules.AtSpawn;
            Run(ref s, 1, 2, 5f);
            Assert.That(s.Phase, Is.EqualTo(NukePhase.Carried));
        }

        [Test]
        public void Defenders_DefuseAnArmedNukeInTheirZone_InTenSeconds()
        {
            var s = NukeRules.AtSpawn;
            Run(ref s, 1, 1, 20f);
            Run(ref s, 0, 0, 1f);  // carrier shot down: dropped, still armed
            Assert.That(s.ArmedBy, Is.EqualTo(1));
            Run(ref s, 2, 2, 5f);
            Run(ref s, 2, 2, 3f, contested: true);
            Assert.That(s.DefuseProgress, Is.EqualTo(0.5f).Within(0.01f), "Attackers in the zone pause the defuse.");
            Assert.That(Run(ref s, 2, 2, 5f), Is.EqualTo(NukeEvent.Defused));
            Assert.That(s.ArmedBy, Is.EqualTo(0));
        }

        [Test]
        public void Rounds_FirstToTwoWins()
        {
            var wins = new FixedList64Bytes<int> { 1, 1 };
            Assert.That(NukeRounds.Winner(wins), Is.EqualTo(0));
            wins[1] = 2;
            Assert.That(NukeRounds.Winner(wins), Is.EqualTo(2));
        }
    }
}
