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

        [Test]
        public void CaptureTheNuke_KillsGiveNoTeamScore()
        {
            Assert.That(TeamRules.ScoresForTeam(MatchMode.CaptureTheNuke, 1, 2), Is.False);
            Assert.That(TeamRules.ScoresForTeam(MatchMode.TeamDeathmatch, 1, 2), Is.True);
        }

        [Test]
        public void Zones_FindTheBaseByHorizontalDistance_AnyHeight()
        {
            var bases = new NativeArray<NukeBase>(2, Allocator.Temp);
            bases[0] = new NukeBase { Team = 1, Radius = 45f, Position = new Unity.Mathematics.float3(-300, 0, 0) };
            bases[1] = new NukeBase { Team = 2, Radius = 45f, Position = new Unity.Mathematics.float3(300, 0, 0) };
            Assert.That(NukeZones.ZoneOf(new Unity.Mathematics.float3(-280, 120, 20), bases), Is.EqualTo(1));
            Assert.That(NukeZones.ZoneOf(new Unity.Mathematics.float3(300, 5, 40), bases), Is.EqualTo(2));
            Assert.That(NukeZones.ZoneOf(new Unity.Mathematics.float3(0, 0, 0), bases), Is.EqualTo(0));
            bases.Dispose();
        }

        [Test]
        public void HangPoint_IsUnderTheCarrier()
        {
            var p = NukeZones.HangPoint(new Unity.Mathematics.float3(1, 50, 2));
            Assert.That(p.y, Is.EqualTo(50 - NukeZones.HangBelow));
            Assert.That(p.x, Is.EqualTo(1));
        }

        [Test]
        public void Hook_NeedsTheFullTimeInReach_AndRestartsForAnotherChopper()
        {
            var a = new Unity.Entities.Entity { Index = 1, Version = 1 };
            var b = new Unity.Entities.Entity { Index = 2, Version = 1 };
            var progress = 0f;
            var hooker = Unity.Entities.Entity.Null;
            for (var i = 0; i < 9; i++) Assert.That(NukeRules.TickHook(ref progress, ref hooker, a, 10f, 1f), Is.False);
            Assert.That(progress, Is.EqualTo(0.9f).Within(1e-4f));
            Assert.That(NukeRules.TickHook(ref progress, ref hooker, b, 10f, 1f), Is.False, "Another chopper starts again.");
            Assert.That(progress, Is.EqualTo(0.1f).Within(1e-4f));
            NukeRules.TickHook(ref progress, ref hooker, Unity.Entities.Entity.Null, 10f, 1f);
            Assert.That(progress, Is.EqualTo(0f), "Nobody in reach clears it.");
            var hooked = false;
            for (var i = 0; i < 10 && !hooked; i++) hooked = NukeRules.TickHook(ref progress, ref hooker, a, 10f, 1f);
            Assert.That(hooked, Is.True);
            Assert.That(hooker, Is.EqualTo(a));
        }

        [Test]
        public void HookConfig_WinchHalvesTheTimeAndReachesFurther()
        {
            var plain = NukeHookConfig.Base;
            var winch = NukeHookConfig.From(0.5f, 1.5f, 1.27f);
            Assert.That(plain.Seconds, Is.EqualTo(10f));
            Assert.That(winch.Seconds, Is.EqualTo(5f));
            Assert.That(winch.Range, Is.EqualTo(plain.Range * 1.5f).Within(1e-4f));
            Assert.That(winch.CarrySpeed, Is.GreaterThan(plain.CarrySpeed));
            Assert.That(NukeHookConfig.From(0f, 0f, 0f).Seconds, Is.EqualTo(10f), "0 = an old asset: no change.");
        }

        [Test]
        public void BaseSpawn_GridFacesThePad_SlotsAreSpacedAndFreeSlotSkipsTaken()
        {
            var home = new Unity.Mathematics.float3(-350, 0, 150);
            var towardsPad = new Unity.Mathematics.float3(350, 0, -530);
            NukeBaseSpawn.Slot(home, towardsPad, 0, out var a, out var rotation);
            NukeBaseSpawn.Slot(home, towardsPad, 1, out var b, out _);
            NukeBaseSpawn.Slot(home, towardsPad, 3, out var backRow, out _);
            Assert.That(Unity.Mathematics.math.distance(a, b), Is.EqualTo(NukeBaseSpawn.Spacing).Within(0.01f));
            Assert.That(a.y, Is.EqualTo(NukeBaseSpawn.Height));
            var forward = Unity.Mathematics.math.mul(rotation, new Unity.Mathematics.float3(0, 0, 1));
            Assert.That(Unity.Mathematics.math.dot(forward, Unity.Mathematics.math.normalize(towardsPad)), Is.GreaterThan(0.99f), "Faces the pad.");
            Assert.That(Unity.Mathematics.math.dot(backRow - a, forward), Is.LessThan(0f), "The second row is behind.");

            var occupied = new NativeArray<Unity.Mathematics.float3>(2, Allocator.Temp);
            occupied[0] = a;
            occupied[1] = b;
            Assert.That(NukeBaseSpawn.FreeSlot(home, towardsPad, occupied), Is.EqualTo(2));
            occupied.Dispose();
        }
    }
}
