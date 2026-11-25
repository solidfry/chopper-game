using Chopper.Presentation.Audio;
using Chopper.Presentation.Hud;
using Chopper.Simulation.Match;
using NUnit.Framework;

namespace Chopper.Tests.EditMode
{
    public class NukeSoundTests
    {
        [Test]
        public void Blast_IsCloseRoarNearAndRumbleFar()
        {
            NukeSound.BlastMix(50f, out var near, out var far);
            Assert.AreEqual(1f, near, 0.001f);
            Assert.Less(far, 0.5f);

            NukeSound.BlastMix(1000f, out near, out far);
            Assert.AreEqual(0f, near, 0.001f);
            Assert.Greater(far, 0.9f);
        }

        [Test]
        public void Blast_WholeMapHearsIt()
        {
            NukeSound.BlastMix(10000f, out var near, out var far);
            Assert.AreEqual(0f, near, 0.001f);
            Assert.AreEqual(NukeSound.FarFloor, far, 0.001f);
        }

        [Test]
        public void Beep_GetsFasterAsTheBarFills()
        {
            Assert.AreEqual(NukeSound.SlowBeep, NukeSound.BeepInterval(0f), 0.001f);
            Assert.AreEqual(NukeSound.FastBeep, NukeSound.BeepInterval(1f), 0.001f);
            Assert.Less(NukeSound.BeepInterval(0.6f), NukeSound.BeepInterval(0.3f));
            Assert.AreEqual(NukeSound.FastBeep, NukeSound.BeepInterval(2f), 0.001f);
        }

        [Test]
        public void Siren_OnlyWhileArmed_LouderForTheThreatenedTeam()
        {
            var nuke = new NukeState { Phase = NukePhase.Carried, ArmedBy = TeamRules.None };
            Assert.AreEqual(0f, NukeSound.SirenVolume(nuke, 1));

            nuke.ArmedBy = 1;
            Assert.AreEqual(NukeSound.OwnSiren, NukeSound.SirenVolume(nuke, 1));
            Assert.AreEqual(1f, NukeSound.SirenVolume(nuke, 2));
            Assert.AreEqual(NukeSound.NeutralSiren, NukeSound.SirenVolume(nuke, TeamRules.None));

            nuke.Phase = NukePhase.Detonated;
            Assert.AreEqual(0f, NukeSound.SirenVolume(nuke, 2));
        }

        [Test]
        public void Beep_OnlyWhileABarGrows()
        {
            var before = new NukeState { Phase = NukePhase.Carried, ArmProgress = 0.4f };
            var now = before;
            Assert.Less(NukeSound.BeepProgress(now, before), 0f, "holding");

            now.ArmProgress = 0.45f;
            Assert.AreEqual(0.45f, NukeSound.BeepProgress(now, before), 0.001f);

            now.ArmProgress = 0.35f;
            Assert.Less(NukeSound.BeepProgress(now, before), 0f, "decaying out of the zone");

            var armed = new NukeState { Phase = NukePhase.Dropped, ArmedBy = 1, DefuseProgress = 0.2f };
            var defusing = armed;
            defusing.DefuseProgress = 0.3f;
            Assert.AreEqual(0.3f, NukeSound.BeepProgress(defusing, armed), 0.001f);

            var free = new NukeState { Phase = NukePhase.AtSpawn };
            var hooking = new NukeState { Phase = NukePhase.AtSpawn, HookTeam = 2, HookProgress = 0.1f };
            Assert.AreEqual(0.1f, NukeSound.BeepProgress(hooking, free), 0.001f);
        }

        [Test]
        public void Stinger_PerChange_NoneForTheBlast()
        {
            Assert.AreEqual(NukeStinger.Hooked, NukeSound.Stinger(NukeChange.PickedUp));
            Assert.AreEqual(NukeStinger.Armed, NukeSound.Stinger(NukeChange.Armed));
            Assert.AreEqual(NukeStinger.Defused, NukeSound.Stinger(NukeChange.Defused));
            Assert.AreEqual(NukeStinger.Reset, NukeSound.Stinger(NukeChange.Reset));
            Assert.AreEqual(NukeStinger.None, NukeSound.Stinger(NukeChange.Detonated));
        }
    }
}
