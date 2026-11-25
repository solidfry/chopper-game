using Chopper.Simulation.Match;
using NUnit.Framework;
using Unity.Collections;

namespace Chopper.Tests
{
    public sealed class NukeRulesTests
    {
        private static readonly NukeTuning Tuning = NukeTuning.Default;

        private static NukeEvent Run(ref NukeState s, byte carrier, byte dock, float seconds, bool contested = false)
        {
            var last = NukeEvent.None;
            var input = new NukeInputs { CarrierTeam = carrier, DockTeam = dock, ZoneContested = contested };
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

        /// <summary>Team 1 hooks the nuke and sets it down in its own dock.</summary>
        private static NukeState Docked1()
        {
            var s = NukeRules.AtSpawn;
            Run(ref s, 1, 0, 1f);
            Assert.That(NukeRules.Tick(ref s, new NukeInputs { CarrierTeam = 1, DockTeam = 1 }, Tuning, 0.1f), Is.EqualTo(NukeEvent.Docked));
            return s;
        }

        [Test]
        public void Dock_ArmsOnlyWhenSetDown_TwentySeconds_PausedWhenContested()
        {
            var s = NukeRules.AtSpawn;
            Run(ref s, 1, 0, 10f);
            Assert.That(s.ArmProgress, Is.EqualTo(0f), "Holding it does not arm it.");
            s = Docked1();
            Assert.That(s.Phase, Is.EqualTo(NukePhase.Docked));
            Assert.That(s.DockTeam, Is.EqualTo(1));
            Assert.That(s.CarrierTeam, Is.EqualTo(0));
            Run(ref s, 0, 0, 10f);
            Assert.That(s.ArmProgress, Is.EqualTo(0.5f).Within(0.02f));
            Run(ref s, 0, 0, 5f, contested: true);
            Assert.That(s.ArmProgress, Is.EqualTo(0.5f).Within(0.02f), "Enemies in the base pause arming.");
            Assert.That(Run(ref s, 0, 0, 10f), Is.EqualTo(NukeEvent.Armed));
            Assert.That(s.ArmedBy, Is.EqualTo(1));
            Assert.That(s.Phase, Is.EqualTo(NukePhase.Docked), "It waits in the dock for its team to lift it.");
        }

        [Test]
        public void Dock_OwnersWaitForTheArm_EnemyCanStealIt_AndTheArmStops()
        {
            var s = Docked1();
            Run(ref s, 0, 0, 10f);
            Assert.That(NukeRules.CanHook(s, 1), Is.False, "Owners cannot lift it while it arms.");
            Assert.That(NukeRules.CanHook(s, 2), Is.True);
            Assert.That(NukeRules.Tick(ref s, new NukeInputs { CarrierTeam = 2 }, Tuning, 0.1f), Is.EqualTo(NukeEvent.Stolen));
            Assert.That(s.ArmProgress, Is.EqualTo(0f));
            Assert.That(s.CarrierTeam, Is.EqualTo(2));
            Assert.That(NukeRules.Tick(ref s, new NukeInputs { CarrierTeam = 2, DockTeam = 1 }, Tuning, 0.1f), Is.EqualTo(NukeEvent.None),
                "Thieves cannot arm it in the other team's dock.");
            Assert.That(NukeRules.Tick(ref s, new NukeInputs { CarrierTeam = 2, DockTeam = 2 }, Tuning, 0.1f), Is.EqualTo(NukeEvent.Docked));
            Assert.That(s.DockTeam, Is.EqualTo(2));
        }

        [Test]
        public void Dock_StealingAnArmedNuke_ClearsTheArm()
        {
            var s = Docked1();
            Run(ref s, 0, 0, 20f);
            Assert.That(NukeRules.CanHook(s, 1) && NukeRules.CanHook(s, 2), Is.True);
            Run(ref s, 2, 0, 0.5f);
            Assert.That(s.ArmedBy, Is.EqualTo(0));
        }

        [Test]
        public void Planted_CountsDown_ThenDetonates()
        {
            var s = Docked1();
            Run(ref s, 0, 0, 20f);
            Run(ref s, 1, 0, 1f);  // the owners lift the armed nuke
            Assert.That(s.ArmedBy, Is.EqualTo(1));
            Assert.That(NukeRules.Tick(ref s, new NukeInputs { CarrierTeam = 1, DockTeam = 2 }, Tuning, 0.1f), Is.EqualTo(NukeEvent.Planted));
            Assert.That(s.Phase, Is.EqualTo(NukePhase.Planted));
            Assert.That(s.Countdown, Is.EqualTo(Tuning.PlantSeconds));
            Assert.That(NukeRules.CanHook(s, 1), Is.False);
            Assert.That(NukeRules.CanHook(s, 2), Is.True, "Defenders can disarm.");
            Assert.That(Run(ref s, 0, 0, Tuning.PlantSeconds), Is.EqualTo(NukeEvent.Detonated));
            Assert.That(s.DetonatedBy, Is.EqualTo(1));
            Assert.That(Run(ref s, 0, 0, 5f), Is.EqualTo(NukeEvent.None), "Nothing happens after the blast.");
        }

        [Test]
        public void Planted_DefendersDisarm_ItBecomesAPlainDrop()
        {
            var s = new NukeState { Phase = NukePhase.Planted, ArmedBy = 1, DockTeam = 2, Countdown = 30f };
            Assert.That(NukeRules.HookSeconds(s, 5f, Tuning), Is.EqualTo(Tuning.DefuseSeconds));
            Assert.That(NukeRules.Tick(ref s, new NukeInputs { Disarmed = true }, Tuning, 0.1f), Is.EqualTo(NukeEvent.Defused));
            Assert.That(s.Phase, Is.EqualTo(NukePhase.Dropped));
            Assert.That(s.ArmedBy, Is.EqualTo(0));
        }

        [Test]
        public void UnarmedNuke_OverTheEnemyDock_DoesNothing()
        {
            var s = NukeRules.AtSpawn;
            Run(ref s, 1, 2, 5f);
            Assert.That(s.Phase, Is.EqualTo(NukePhase.Carried));
        }

        [Test]
        public void Zones_DockNeedsTheNukeLowOverIt()
        {
            var bases = new NativeArray<NukeBase>(1, Allocator.Temp);
            bases[0] = new NukeBase { Team = 1, Radius = 120f, Position = default, Dock = new Unity.Mathematics.float3(20, 0, 0) };
            Assert.That(NukeZones.DockOf(new Unity.Mathematics.float3(24, 8, 0), bases), Is.EqualTo(0));
            Assert.That(NukeZones.DockOf(new Unity.Mathematics.float3(24, 30, 0), bases), Is.EqualTo(-1), "Too high.");
            Assert.That(NukeZones.DockOf(new Unity.Mathematics.float3(0, 5, 0), bases), Is.EqualTo(-1), "Too far.");
            bases.Dispose();
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
        public void BaseSpawn_BaysFacePad_AwayFromTheDock_SpacedAndFreeSlotSkipsTaken()
        {
            var home = new Unity.Mathematics.float3(-350, 0, 150);
            var towardsPad = new Unity.Mathematics.float3(350, 0, -530);
            NukeBaseSpawn.Slot(home, towardsPad, 0, out var a, out var rotation);
            NukeBaseSpawn.Slot(home, towardsPad, 1, out var b, out _);
            Assert.That(a.y, Is.EqualTo(NukeBaseSpawn.Height));
            var forward = Unity.Mathematics.math.mul(rotation, new Unity.Mathematics.float3(0, 0, 1));
            Assert.That(Unity.Mathematics.math.dot(forward, Unity.Mathematics.math.normalize(towardsPad)), Is.GreaterThan(0.99f), "Faces the pad.");
            for (var i = 0; i < NukeBaseSpawn.MaxSlots; i++)
            {
                NukeBaseSpawn.Slot(home, towardsPad, i, out var p, out _);
                Assert.That(Unity.Mathematics.math.distance(p.xz, home.xz), Is.GreaterThan(40f), "Not on the dock.");
                for (var k = 0; k < i; k++)
                {
                    NukeBaseSpawn.Slot(home, towardsPad, k, out var q, out _);
                    Assert.That(Unity.Mathematics.math.distance(p, q), Is.GreaterThanOrEqualTo(NukeBaseSpawn.Spacing - 0.01f));
                }
            }

            var occupied = new NativeArray<Unity.Mathematics.float3>(2, Allocator.Temp);
            occupied[0] = a;
            occupied[1] = b;
            Assert.That(NukeBaseSpawn.FreeSlot(home, towardsPad, occupied), Is.EqualTo(2));
            occupied.Dispose();
        }

        [Test]
        public void Carry_ScalesTopSpeedThroughDrag_OnlyWhileCarrying()
        {
            Assert.That(NukeCarry.DragScale(new NukeCarry { Carrying = false, SpeedScale = 0.5f }), Is.EqualTo(1f));
            Assert.That(NukeCarry.DragScale(new NukeCarry { Carrying = true, SpeedScale = 0.5f }), Is.EqualTo(4f).Within(1e-4f));
            Assert.That(NukeCarry.DragScale(new NukeCarry { Carrying = true, SpeedScale = 0f }), Is.EqualTo(1f), "Unset: no change.");
        }

        [Test]
        public void BotObjective_HookThenArmThenDeliver_ChaseAndEscort()
        {
            var self = new Unity.Entities.Entity { Index = 5, Version = 1 };
            var other = new Unity.Entities.Entity { Index = 6, Version = 1 };
            var info = new Chopper.Simulation.Bots.BotObjectiveInfo
            {
                Active = true, NukePosition = new Unity.Mathematics.float3(0, 1, -380), Phase = NukePhase.AtSpawn,
                Base1 = new Unity.Mathematics.float3(-350, 0, 150), Base2 = new Unity.Mathematics.float3(350, 0, 150),
            };
            Assert.That(Chopper.Simulation.Bots.BotObjective.Choose(info, self, 1, out var goal, out _), Is.EqualTo(Chopper.Simulation.Bots.BotObjective.Kind.Hook));
            Assert.That(goal.y, Is.EqualTo(9f).Within(1e-4f));

            info.Carrier = self; info.CarrierTeam = 1; info.Phase = NukePhase.Carried;
            Assert.That(Chopper.Simulation.Bots.BotObjective.Choose(info, self, 1, out goal, out _), Is.EqualTo(Chopper.Simulation.Bots.BotObjective.Kind.ArmAtHome));
            Assert.That(goal.x, Is.EqualTo(-350f));
            info.ArmedBy = 1;
            Assert.That(Chopper.Simulation.Bots.BotObjective.Choose(info, self, 1, out goal, out _), Is.EqualTo(Chopper.Simulation.Bots.BotObjective.Kind.Deliver));
            Assert.That(goal.x, Is.EqualTo(350f));

            Assert.That(Chopper.Simulation.Bots.BotObjective.Choose(info, other, 1, out _, out _), Is.EqualTo(Chopper.Simulation.Bots.BotObjective.Kind.Escort));
            Assert.That(Chopper.Simulation.Bots.BotObjective.Choose(info, other, 2, out _, out _), Is.EqualTo(Chopper.Simulation.Bots.BotObjective.Kind.Chase));

            // Docked and arming in team 1's dock: team 1 guards it, team 2 goes to steal it.
            info.Carrier = Unity.Entities.Entity.Null; info.CarrierTeam = 0; info.ArmedBy = 0; info.Phase = NukePhase.Docked; info.DockTeam = 1;
            Assert.That(Chopper.Simulation.Bots.BotObjective.Choose(info, self, 1, out _, out _), Is.EqualTo(Chopper.Simulation.Bots.BotObjective.Kind.Escort));
            Assert.That(Chopper.Simulation.Bots.BotObjective.Choose(info, self, 2, out _, out _), Is.EqualTo(Chopper.Simulation.Bots.BotObjective.Kind.Hook));
            info.Active = false;
            Assert.That(Chopper.Simulation.Bots.BotObjective.Choose(info, self, 1, out _, out _), Is.EqualTo(Chopper.Simulation.Bots.BotObjective.Kind.None));
        }

        [Test]
        public void BotObjective_Siege_HoldsOutOfReachWhileTurretsCoverTheNuke()
        {
            var info = new Chopper.Simulation.Bots.BotObjectiveInfo
            {
                Active = true, NukePosition = new Unity.Mathematics.float3(0, 1, -380),
                Base1 = new Unity.Mathematics.float3(-350, 0, 150), Base2 = new Unity.Mathematics.float3(350, 0, 150),
            };
            var turrets = new Unity.Collections.NativeArray<Chopper.Simulation.Bots.BotTurretInfo>(1, Unity.Collections.Allocator.Temp);
            turrets[0] = new Chopper.Simulation.Bots.BotTurretInfo { Position = new Unity.Mathematics.float3(31, 2, -349), Team = 0, Reach = 260f };
            Assert.That(Chopper.Simulation.Bots.BotObjective.TurretsCover(info, turrets, 1), Is.True, "Neutral pad turret 44 m from the nuke.");

            Assert.That(Chopper.Simulation.Bots.BotObjective.Stage(info, 1, true, out var staging), Is.True);
            Assert.That(Unity.Mathematics.math.distance(staging.xz, info.NukePosition.xz), Is.EqualTo(330f).Within(0.1f));
            Assert.That(staging.x, Is.LessThan(0f), "On the home side.");
            Assert.That(Unity.Mathematics.math.distance(staging.xz, turrets[0].Position.xz), Is.GreaterThan(turrets[0].Reach), "Out of the turret's reach.");
            Assert.That(Chopper.Simulation.Bots.BotObjective.Stage(info, 1, false, out _), Is.False, "No cover: go in and hook.");

            turrets[0] = new Chopper.Simulation.Bots.BotTurretInfo { Position = new Unity.Mathematics.float3(-350, 2, 150), Team = 2, Reach = 260f };
            Assert.That(Chopper.Simulation.Bots.BotObjective.TurretsCover(info, turrets, 1), Is.False, "A base turret far from the nuke does not cover it.");
            turrets[0] = new Chopper.Simulation.Bots.BotTurretInfo { Position = new Unity.Mathematics.float3(31, 2, -349), Team = 1, Reach = 260f };
            Assert.That(Chopper.Simulation.Bots.BotObjective.TurretsCover(info, turrets, 1), Is.False, "Own team's turret.");
            turrets.Dispose();
        }

        [Test]
        public void BotBrain_StrafeRange_CirclesATurretFromOutsideItsReach()
        {
            Assert.That(Chopper.Simulation.Bots.BotBrain.StrafeRange(220f, 600f, 0f), Is.EqualTo(220f), "Chopper target: preferred range.");
            Assert.That(Chopper.Simulation.Bots.BotBrain.StrafeRange(220f, 600f, 260f), Is.EqualTo(260f + Chopper.Simulation.Bots.BotBrain.StandOffMargin), "Minigun outranges the turret.");
            Assert.That(Chopper.Simulation.Bots.BotBrain.StrafeRange(220f, 250f, 260f), Is.EqualTo(225f), "Short gun: as far as it still reaches.");
            Assert.That(Chopper.Simulation.Bots.BotBrain.StrafeRange(220f, 200f, 260f), Is.EqualTo(220f), "Never closer than the preferred range.");
        }
    }
}
