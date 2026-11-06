using Chopper.Presentation.Hud;
using Chopper.Simulation.Combat;
using NUnit.Framework;

namespace Chopper.Tests
{
    public sealed class WeaponLimitsTests
    {
        private const float Tick = 1f / 60f;

        /// <summary>A gun at 60 shots per second, so one shot per tick keeps the counts simple.</summary>
        private static HitscanWeaponConfig Gun(float heatSeconds = 2f, float coolSeconds = 4f) => new()
        {
            FireInterval = Tick,
            HeatPerShot = Tick / heatSeconds,
            CoolRate = 1f / coolSeconds,
        };

        private static int ShotsToOverheat(ref HitscanWeaponState state, in HitscanWeaponConfig config)
        {
            var shots = 0;
            while (GunHeat.CanFire(state) && shots < 10000)
            {
                GunHeat.Tick(ref state, config, Tick);
                GunHeat.AddShot(ref state, config);
                shots++;
            }
            return shots;
        }

        [Test]
        public void Gun_SteadyFire_OverheatsAfterHeatSeconds()
        {
            var state = new HitscanWeaponState();
            var shots = ShotsToOverheat(ref state, Gun(heatSeconds: 2f));

            Assert.That(shots, Is.InRange(120, 121), "2 seconds at 60 shots per second");
            Assert.That(state.Overheated, Is.True);
        }

        [Test]
        public void Gun_Overheated_LocksOutUntilResumeHeat()
        {
            var config = Gun(coolSeconds: 4f);
            var state = new HitscanWeaponState();
            ShotsToOverheat(ref state, config);

            // Full to ResumeHeat: 0.6 of the 4 second cool time.
            for (var i = 0; i < 140; i++) GunHeat.Tick(ref state, config, Tick);
            Assert.That(GunHeat.CanFire(state), Is.False, "Still locked out at 2.33 s");

            for (var i = 0; i < 10; i++) GunHeat.Tick(ref state, config, Tick);
            Assert.That(GunHeat.CanFire(state), Is.True, "Fires again by 2.5 s");
            Assert.That(state.Heat, Is.EqualTo(GunHeat.ResumeHeat).Within(0.03f));
        }

        [Test]
        public void Gun_StartsToCoolOnlyAfterThePause()
        {
            var config = Gun();
            var state = new HitscanWeaponState();
            GunHeat.AddShot(ref state, config);
            var heat = state.Heat;

            GunHeat.Tick(ref state, config, Tick);
            Assert.That(state.Heat, Is.EqualTo(heat), "No cooling just after a shot");

            for (var i = 0; i < 20; i++) GunHeat.Tick(ref state, config, Tick);
            Assert.That(state.Heat, Is.LessThan(heat), "Cooling after the pause");
        }

        [Test]
        public void Gun_NoHeatLimit_NeverOverheats()
        {
            var config = new HitscanWeaponConfig { FireInterval = Tick };
            var state = new HitscanWeaponState();
            for (var i = 0; i < 2000; i++)
            {
                GunHeat.Tick(ref state, config, Tick);
                GunHeat.AddShot(ref state, config);
            }

            Assert.That(GunHeat.CanFire(state), Is.True);
            Assert.That(state.Heat, Is.Zero);
        }

        [Test]
        public void Magazine_DefaultState_IsFull()
        {
            var state = new PylonWeaponState();
            Assert.That(Magazine.RoundsLeft(state.RoundsUsed, 14), Is.EqualTo(14));
            Assert.That(Magazine.HasRound(state.RoundsUsed, 14), Is.True);
        }

        [Test]
        public void Magazine_Empty_StopsFiringUntilReload()
        {
            byte used = 0;
            var reload = 0f;
            for (var i = 0; i < 2; i++) Magazine.Spend(ref used, ref reload, 2, 6f);
            Assert.That(Magazine.HasRound(used, 2), Is.False);

            Magazine.Tick(ref used, ref reload, 5.9f);
            Assert.That(Magazine.HasRound(used, 2), Is.False, "Not refilled before the reload time");

            Magazine.Tick(ref used, ref reload, 0.2f);
            Assert.That(used, Is.Zero, "Refills fully");
            Assert.That(reload, Is.Zero);
        }

        [Test]
        public void Magazine_EachShotRestartsTheReload()
        {
            byte used = 0;
            var reload = 0f;
            Magazine.Spend(ref used, ref reload, 14, 6f);
            Magazine.Tick(ref used, ref reload, 5f);
            Magazine.Spend(ref used, ref reload, 14, 6f);
            Magazine.Tick(ref used, ref reload, 5f);

            Assert.That(used, Is.EqualTo(2), "A shot 5 s in restarts the 6 s reload");

            Magazine.Tick(ref used, ref reload, 1.1f);
            Assert.That(used, Is.Zero);
        }

        [Test]
        public void Magazine_SizeZero_HasNoLimit()
        {
            byte used = 0;
            var reload = 0f;
            for (var i = 0; i < 300; i++) Magazine.Spend(ref used, ref reload, 0, 6f);

            Assert.That(used, Is.Zero);
            Assert.That(Magazine.HasRound(used, 0), Is.True);
        }

        [Test]
        public void Magazine_ReloadProgress_RunsFromShotToFull()
        {
            Assert.That(Magazine.ReloadProgress(0, 0f, 6f), Is.EqualTo(1f), "Full magazine");
            Assert.That(Magazine.ReloadProgress(3, 6f, 6f), Is.Zero, "Just fired");
            Assert.That(Magazine.ReloadProgress(3, 1.5f, 6f), Is.EqualTo(0.75f).Within(1e-4f));
            Assert.That(Magazine.ReloadProgress(3, 1f, 0f), Is.EqualTo(1f), "No reload time");
        }

        [Test]
        public void Hud_ReloadProgress_StepsAndDefaultsToFull()
        {
            var model = new ChopperHudModel();
            Assert.That(model.PylonReload, Is.EqualTo(1f));

            model.UpdateWeaponLimits(true, 0f, false, 9, 14, 2, 3, 0.503f, 0.25f);
            Assert.That(model.PylonReload, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(model.FlareReload, Is.EqualTo(0.24f).Within(0.021f));

            model.ClearChopper();
            Assert.That(model.PylonReload, Is.EqualTo(1f));
            Assert.That(model.FlareReload, Is.EqualTo(1f));
        }

        [Test]
        public void Hud_ShowsRoundsReloadAndHeat()
        {
            var model = new ChopperHudModel();

            model.UpdateWeaponLimits(true, 0.42f, false, 9, 14, 2, 3);
            Assert.That(model.PylonAmmoText, Is.EqualTo("9/14"));
            Assert.That(model.PylonEmpty, Is.False);
            Assert.That(model.FlaresText, Is.EqualTo("FLARES 2"));
            Assert.That(model.GunHeat, Is.EqualTo(0.4f).Within(0.001f), "Steps of 5%");
            Assert.That(model.GunHeatText, Is.EqualTo("GUN 40%"));

            model.UpdateWeaponLimits(true, 1f, true, 0, 14, 0, 3);
            Assert.That(model.PylonAmmoText, Is.EqualTo("RELOAD"));
            Assert.That(model.PylonEmpty, Is.True);
            Assert.That(model.FlaresText, Is.EqualTo("FLARES 0"));
            Assert.That(model.GunOverheated, Is.True);
            Assert.That(model.GunHeatText, Is.EqualTo("GUN HOT"));
        }

        [Test]
        public void Hud_NoLimits_ShowsNoNumbers()
        {
            var model = new ChopperHudModel();
            model.UpdateWeaponLimits(true, 0.5f, false, 9, 14, 2, 3);

            model.UpdateWeaponLimits(false, 0f, false, 0, 0, 0, 0);

            Assert.That(model.PylonAmmoText, Is.Empty);
            Assert.That(model.PylonEmpty, Is.False);
            Assert.That(model.FlaresText, Is.EqualTo("FLARES"));
            Assert.That(model.GunHeatText, Is.Empty);
        }
    }
}
