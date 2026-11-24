using Chopper.Presentation.Hud;
using Chopper.Simulation.Match;
using NUnit.Framework;

namespace Chopper.Tests
{
    public class NukeCalloutTests
    {
        private const byte Us = 1;
        private const byte Them = 2;
        private const float Reset = 30f;

        private static NukeState Carried(byte team, byte armedBy = 0) =>
            new() { Phase = NukePhase.Carried, CarrierTeam = team, ArmedBy = armedBy };

        [Test]
        public void NoTeamOrDetonated_ShowsNothing()
        {
            Assert.AreEqual(CalloutTone.None, NukeCallout.Status(NukeRules.AtSpawn, 0, 0, false, false, Reset).Tone);
            var gone = new NukeState { Phase = NukePhase.Detonated, DetonatedBy = Us };
            Assert.AreEqual("", NukeCallout.Status(gone, Us, 0, false, false, Reset).Text);
        }

        [Test]
        public void FreeNuke_TellsYouToHook_ThenHoldInRange()
        {
            Assert.AreEqual("HOOK THE NUKE", NukeCallout.Status(NukeRules.AtSpawn, Us, 0, false, false, Reset).Text);
            var near = NukeCallout.Status(NukeRules.AtSpawn, Us, 0, false, true, Reset);
            Assert.AreEqual("HOLD POSITION TO HOOK", near.Text);
            Assert.AreEqual(CalloutTone.Good, near.Tone);
        }

        [Test]
        public void Hooking_ShowsPercentAndBar_EnemyHookingIsAlert()
        {
            var s = new NukeState { Phase = NukePhase.AtSpawn, HookTeam = Us, HookProgress = 0.4f };
            var mine = NukeCallout.Status(s, Us, 0, false, true, Reset);
            Assert.AreEqual("HOOKING  40%", mine.Text);
            Assert.AreEqual(0.4f, mine.Progress, 1e-5f);

            s.HookTeam = Them;
            var theirs = NukeCallout.Status(s, Us, 0, false, false, Reset);
            Assert.AreEqual(CalloutTone.Alert, theirs.Tone);
            Assert.AreEqual(0.4f, theirs.Progress, 1e-5f);
        }

        [Test]
        public void Dropped_CountsDownToReset()
        {
            var s = new NukeState { Phase = NukePhase.Dropped, DroppedFor = 12.3f };
            Assert.AreEqual("NUKE DROPPED · RESET IN 18", NukeCallout.Status(s, Us, 0, false, false, Reset).Text);
            s.ArmedBy = Us;
            Assert.AreEqual(CalloutTone.Alert, NukeCallout.Status(s, Us, 0, false, false, Reset).Tone);
            s.ArmedBy = Them;
            Assert.AreEqual(CalloutTone.Good, NukeCallout.Status(s, Us, 0, false, false, Reset).Tone);
        }

        [Test]
        public void CarriedUnarmed_FlyHome_ThenArmInOwnZone()
        {
            Assert.AreEqual("FLY THE NUKE TO YOUR BASE", NukeCallout.Status(Carried(Us), Us, 0, true, false, Reset).Text);
            Assert.AreEqual("ESCORT YOUR CARRIER HOME", NukeCallout.Status(Carried(Us), Us, 0, false, false, Reset).Text);
            var s = Carried(Us);
            s.ArmProgress = 0.25f;
            var arming = NukeCallout.Status(s, Us, Us, true, false, Reset);
            Assert.AreEqual("ARMING  25% · HOLD IN BASE", arming.Text);
            Assert.AreEqual(0.25f, arming.Progress, 1e-5f);
        }

        [Test]
        public void EnemyCarrier_IsAlert_ArmingInTheirZoneShowsBar()
        {
            Assert.AreEqual("ENEMY HAS THE NUKE · SHOOT THE CARRIER", NukeCallout.Status(Carried(Them), Us, 0, false, false, Reset).Text);
            var s = Carried(Them);
            s.ArmProgress = 0.5f;
            var c = NukeCallout.Status(s, Us, Them, false, false, Reset);
            Assert.AreEqual(CalloutTone.Alert, c.Tone);
            Assert.AreEqual(0.5f, c.Progress, 1e-5f);
        }

        [Test]
        public void Armed_AttackersFlyToEnemy_DefendersDefend()
        {
            Assert.AreEqual("NUKE ARMED · FLY TO THE ENEMY BASE", NukeCallout.Status(Carried(Us, Us), Us, 0, true, false, Reset).Text);
            Assert.AreEqual("ARMED NUKE INBOUND · DEFEND YOUR BASE", NukeCallout.Status(Carried(Them, Them), Us, 0, false, false, Reset).Text);
        }

        [Test]
        public void Defuse_BothSides()
        {
            var s = Carried(Us, Them);
            Assert.AreEqual("TAKE IT TO YOUR BASE TO DEFUSE", NukeCallout.Status(s, Us, 0, true, false, Reset).Text);
            s.DefuseProgress = 0.6f;
            Assert.AreEqual("DEFUSING  60%", NukeCallout.Status(s, Us, Us, true, false, Reset).Text);
            // Seen by the team that armed it.
            Assert.AreEqual(CalloutTone.Alert, NukeCallout.Status(s, Them, Us, false, false, Reset).Tone);
        }

        [Test]
        public void Diff_FindsEachChange()
        {
            byte team;
            Assert.AreEqual(NukeChange.PickedUp, NukeCallout.Diff(NukeRules.AtSpawn, Carried(Us), out team));
            Assert.AreEqual(Us, team);
            Assert.AreEqual(NukeChange.PickedUp, NukeCallout.Diff(Carried(Us), Carried(Them), out team));
            Assert.AreEqual(Them, team);
            Assert.AreEqual(NukeChange.Armed, NukeCallout.Diff(Carried(Us), Carried(Us, Us), out team));
            Assert.AreEqual(NukeChange.Defused, NukeCallout.Diff(Carried(Them, Us), Carried(Them), out team));
            Assert.AreEqual(Them, team);
            Assert.AreEqual(NukeChange.Dropped, NukeCallout.Diff(Carried(Us), new NukeState { Phase = NukePhase.Dropped }, out team));
            Assert.AreEqual(Us, team);
            Assert.AreEqual(NukeChange.Reset, NukeCallout.Diff(new NukeState { Phase = NukePhase.Dropped }, NukeRules.AtSpawn, out _));
            Assert.AreEqual(NukeChange.Detonated,
                NukeCallout.Diff(Carried(Us, Us), new NukeState { Phase = NukePhase.Detonated, DetonatedBy = Us }, out team));
            Assert.AreEqual(Us, team);
            // Next round starts: no flash.
            Assert.AreEqual(NukeChange.None, NukeCallout.Diff(new NukeState { Phase = NukePhase.Detonated }, NukeRules.AtSpawn, out _));
            Assert.AreEqual(NukeChange.None, NukeCallout.Diff(Carried(Us), Carried(Us), out _));
        }

        [Test]
        public void Flash_ReadsFromOurSide()
        {
            Assert.AreEqual(CalloutTone.Good, NukeCallout.Flash(NukeChange.Armed, Us, Us).Tone);
            Assert.AreEqual("ENEMY ARMED THE NUKE", NukeCallout.Flash(NukeChange.Armed, Them, Us).Text);
            Assert.AreEqual(CalloutTone.Good, NukeCallout.Flash(NukeChange.Dropped, Them, Us).Tone);
            Assert.AreEqual("ENEMY BASE DESTROYED · ROUND WON", NukeCallout.Flash(NukeChange.Detonated, Us, Us).Text);
            Assert.AreEqual(CalloutTone.Alert, NukeCallout.Flash(NukeChange.Detonated, Them, Us).Tone);
            Assert.AreEqual("", NukeCallout.Flash(NukeChange.None, Us, Us).Text);
        }

        [Test]
        public void Model_FlashReplacesLine_ThenStatusReturns()
        {
            var model = new MatchHudModel();
            model.SetObjective(new Callout("HOOK THE NUKE", CalloutTone.Neutral));
            model.FlashObjective(new Callout("NUKE ARMED", CalloutTone.Good));
            Assert.AreEqual("NUKE ARMED", model.ObjectiveText);
            Assert.IsTrue(model.ObjectiveFlashing);
            model.SetObjective(new Callout("ARMING  10%", CalloutTone.Good, 0.1f));
            Assert.AreEqual("NUKE ARMED", model.ObjectiveText);
            Assert.AreEqual(-1f, model.ObjectiveProgress);
            model.Tick(MatchHudModel.ObjectiveFlashSeconds + 0.1f);
            Assert.AreEqual("ARMING  10%", model.ObjectiveText);
            Assert.AreEqual(0.1f, model.ObjectiveProgress, 1e-5f);
        }
    }
}
