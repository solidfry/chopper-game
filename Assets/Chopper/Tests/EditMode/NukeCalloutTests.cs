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

        private static NukeState Docked(byte team, float arm = 0.5f, byte armedBy = 0) =>
            new() { Phase = NukePhase.Docked, DockTeam = team, ArmProgress = arm, ArmedBy = armedBy };

        private static NukeState Planted(byte defenders, float countdown = 30f, float disarm = 0f) =>
            new()
            {
                Phase = NukePhase.Planted, DockTeam = defenders, ArmedBy = (byte)(3 - defenders), Countdown = countdown,
                DefuseProgress = disarm, HookTeam = disarm > 0f ? defenders : (byte)0,
            };

        [Test]
        public void NoTeamOrDetonated_ShowsNothing()
        {
            Assert.AreEqual(CalloutTone.None, NukeCallout.Status(NukeRules.AtSpawn, 0, false, false, Reset).Tone);
            var gone = new NukeState { Phase = NukePhase.Detonated };
            Assert.AreEqual("", NukeCallout.Status(gone, Us, false, false, Reset).Text);
        }

        [Test]
        public void FreeNuke_TellsYouToHook_ThenHoldInRange()
        {
            Assert.AreEqual("HOOK THE NUKE", NukeCallout.Status(NukeRules.AtSpawn, Us, false, false, Reset).Text);
            Assert.AreEqual("HOLD POSITION TO HOOK", NukeCallout.Status(NukeRules.AtSpawn, Us, false, true, Reset).Text);
        }

        [Test]
        public void Hooking_ShowsPercentAndBar_EnemyHookingIsAlert()
        {
            var s = new NukeState { Phase = NukePhase.AtSpawn, HookTeam = Us, HookProgress = 0.4f };
            var mine = NukeCallout.Status(s, Us, false, true, Reset);
            Assert.AreEqual("HOOKING  40%", mine.Text);
            Assert.AreEqual(0.4f, mine.Progress, 1e-5f);
            s.HookTeam = Them;
            Assert.AreEqual(CalloutTone.Alert, NukeCallout.Status(s, Us, false, false, Reset).Tone);
        }

        [Test]
        public void Dropped_CountsDownToReset()
        {
            var s = new NukeState { Phase = NukePhase.Dropped, DroppedFor = 12f };
            Assert.AreEqual("NUKE DROPPED · RESET IN 18", NukeCallout.Status(s, Us, false, false, Reset).Text);
        }

        [Test]
        public void Carried_SetItDownInYourDock_ArmedPlantItInTheEnemyDock()
        {
            Assert.AreEqual("SET THE NUKE DOWN IN YOUR BASE DOCK", NukeCallout.Status(Carried(Us), Us, true, false, Reset).Text);
            Assert.AreEqual("ESCORT YOUR CARRIER HOME", NukeCallout.Status(Carried(Us), Us, false, false, Reset).Text);
            Assert.AreEqual(CalloutTone.Alert, NukeCallout.Status(Carried(Them), Us, false, false, Reset).Tone);
            Assert.AreEqual("NUKE ARMED · PLANT IT IN THE ENEMY DOCK", NukeCallout.Status(Carried(Us, Us), Us, true, false, Reset).Text);
            Assert.AreEqual("ARMED NUKE INBOUND · SHOOT THE CARRIER", NukeCallout.Status(Carried(Them, Them), Us, false, false, Reset).Text);
        }

        [Test]
        public void Docked_OwnersDefend_EnemiesSteal()
        {
            var ours = NukeCallout.Status(Docked(Us), Us, false, false, Reset);
            Assert.AreEqual("ARMING  50% · DEFEND THE DOCK", ours.Text);
            Assert.AreEqual(0.5f, ours.Progress, 1e-5f);
            Assert.AreEqual("ENEMY ARMING  50% · STEAL THE NUKE", NukeCallout.Status(Docked(Them), Us, false, false, Reset).Text);
            Assert.AreEqual("HOLD POSITION TO STEAL", NukeCallout.Status(Docked(Them), Us, false, true, Reset).Text);
            Assert.AreEqual("NUKE ARMED · HOOK IT FROM THE DOCK", NukeCallout.Status(Docked(Us, 1f, Us), Us, false, false, Reset).Text);
            var stealing = Docked(Us);
            stealing.HookTeam = Them;
            stealing.HookProgress = 0.3f;
            Assert.AreEqual("ENEMY STEALING YOUR NUKE · STOP THEM", NukeCallout.Status(stealing, Us, false, false, Reset).Text);
        }

        [Test]
        public void Planted_DefendersDisarm_AttackersDefend()
        {
            Assert.AreEqual("NUKE PLANTED IN YOUR BASE · DISARM IT", NukeCallout.Status(Planted(Us), Us, false, false, Reset).Text);
            Assert.AreEqual("HOLD POSITION TO DISARM", NukeCallout.Status(Planted(Us), Us, false, true, Reset).Text);
            Assert.AreEqual("DISARMING  60%", NukeCallout.Status(Planted(Us, disarm: 0.6f), Us, false, true, Reset).Text);
            Assert.AreEqual("NUKE PLANTED · DEFEND IT", NukeCallout.Status(Planted(Them), Us, false, false, Reset).Text);
            Assert.AreEqual(CalloutTone.Alert, NukeCallout.Status(Planted(Them, disarm: 0.2f), Us, false, false, Reset).Tone);
        }

        [Test]
        public void Timer_ArmingFinalCountdownAndReset_ForEveryone()
        {
            var t = NukeCallout.Timer(Docked(Us, 0.25f), Us, NukeTuning.Default);
            Assert.AreEqual("ARMING", t.Label);
            Assert.AreEqual("0:15", t.Clock);
            Assert.AreEqual(CalloutTone.Good, t.Tone);
            Assert.AreEqual("ENEMY ARMING", NukeCallout.Timer(Docked(Them), Us, NukeTuning.Default).Label);
            var final = NukeCallout.Timer(Planted(Us, 44.2f), Us, NukeTuning.Default);
            Assert.IsTrue(final.Final);
            Assert.AreEqual("0:45", final.Clock);
            Assert.AreEqual(CalloutTone.Alert, final.Tone);
            Assert.AreEqual(CalloutTone.Good, NukeCallout.Timer(Planted(Them), Us, NukeTuning.Default).Tone);
            Assert.IsFalse(NukeCallout.Timer(Carried(Us), Us, NukeTuning.Default).Shown);
            Assert.IsFalse(NukeCallout.Timer(Docked(Us, 1f, Us), Us, NukeTuning.Default).Shown, "Armed and waiting: no clock.");
            var noTeam = NukeCallout.Timer(Docked(Them), TeamRules.None, NukeTuning.Default);
            Assert.AreEqual("ARMING", noTeam.Label, "No team: never 'enemy'.");
            Assert.AreEqual(CalloutTone.Neutral, noTeam.Tone);
            Assert.AreEqual(CalloutTone.Neutral, NukeCallout.Timer(Planted(Them), TeamRules.None, NukeTuning.Default).Tone);
        }

        [Test]
        public void Diff_FindsEachChange()
        {
            byte team;
            Assert.AreEqual(NukeChange.PickedUp, NukeCallout.Diff(NukeRules.AtSpawn, Carried(Us), out team));
            Assert.AreEqual(Us, team);
            Assert.AreEqual(NukeChange.Docked, NukeCallout.Diff(Carried(Us), Docked(Us, 0f), out team));
            Assert.AreEqual(Us, team);
            Assert.AreEqual(NukeChange.Armed, NukeCallout.Diff(Docked(Us), Docked(Us, 1f, Us), out team));
            Assert.AreEqual(NukeChange.Stolen, NukeCallout.Diff(Docked(Us), Carried(Them), out team));
            Assert.AreEqual(Them, team);
            Assert.AreEqual(NukeChange.PickedUp, NukeCallout.Diff(Docked(Us, 1f, Us), Carried(Us, Us), out team));
            Assert.AreEqual(NukeChange.Planted, NukeCallout.Diff(Carried(Us, Us), Planted(Them), out team));
            Assert.AreEqual(Us, team);
            Assert.AreEqual(NukeChange.Defused, NukeCallout.Diff(Planted(Them), new NukeState { Phase = NukePhase.Dropped }, out team));
            Assert.AreEqual(NukeChange.None, NukeCallout.Diff(Planted(Them), NukeRules.AtSpawn, out _),
                "A blast resets the nuke to the pad in the same tick: not a defuse.");
            Assert.AreEqual(Them, team);
            Assert.AreEqual(NukeChange.Dropped, NukeCallout.Diff(Carried(Us), new NukeState { Phase = NukePhase.Dropped }, out team));
            Assert.AreEqual(NukeChange.Reset, NukeCallout.Diff(new NukeState { Phase = NukePhase.Dropped }, NukeRules.AtSpawn, out _));
            Assert.AreEqual(NukeChange.Detonated,
                NukeCallout.Diff(Planted(Them), new NukeState { Phase = NukePhase.Detonated, DetonatedBy = Us }, out team));
            Assert.AreEqual(Us, team);
            Assert.AreEqual(NukeChange.None, NukeCallout.Diff(new NukeState { Phase = NukePhase.Detonated }, NukeRules.AtSpawn, out _));
            Assert.AreEqual(NukeChange.None, NukeCallout.Diff(Carried(Us), Carried(Us), out _));
        }

        [Test]
        public void Flash_ReadsFromOurSide()
        {
            Assert.AreEqual(CalloutTone.Good, NukeCallout.Flash(NukeChange.Armed, Us, Us).Tone);
            Assert.AreEqual("ENEMY ARMED THE NUKE", NukeCallout.Flash(NukeChange.Armed, Them, Us).Text);
            Assert.AreEqual(CalloutTone.Good, NukeCallout.Flash(NukeChange.Dropped, Them, Us).Tone);
            Assert.AreEqual("ENEMY STOLE YOUR NUKE", NukeCallout.Flash(NukeChange.Stolen, Them, Us).Text);
            Assert.AreEqual("NUKE PLANTED IN YOUR BASE · DISARM IT", NukeCallout.Flash(NukeChange.Planted, Them, Us).Text);
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

        [Test]
        public void Model_NukeTimer_ShowsLabelAndClock_HidesWhenNone()
        {
            var model = new MatchHudModel();
            model.SetNukeTimer(new NukeTimer("DETONATION", 9.2f, CalloutTone.Alert, final: true));
            Assert.AreEqual("DETONATION  0:10", model.NukeTimerText);
            Assert.IsTrue(model.NukeTimerFinal);
            model.SetNukeTimer(NukeTimer.None);
            Assert.AreEqual("", model.NukeTimerText);
        }
    }
}
