using Chopper.Presentation.Flow;
using Chopper.Presentation.Hud;
using Chopper.Simulation.Combat;
using NUnit.Framework;

namespace Chopper.Tests
{
    public sealed class RepairTests
    {
        private static readonly RepairConfig Config = RepairConfig.Default;

        [Test]
        public void HoldingRepairs_FromEmptyToFull_InTheConfiguredTime()
        {
            var repair = new ChopperRepair();
            var health = new Health { Current = 1f, Max = 100f };
            // 10 s at 60 ticks a second.
            for (var i = 0; i < 600; i++) Repair.Step(ref repair, Config, ref health, held: true, 1f / 60f);

            Assert.That(health.Current, Is.EqualTo(100f).Within(1e-3f));
            Assert.That(repair.State, Is.EqualTo(RepairPhase.Idle), "full: the repair stops by itself");

            health.Current = 50f;
            Repair.Step(ref repair, Config, ref health, held: true, 1f);
            Assert.That(health.Current, Is.EqualTo(60f).Within(1e-3f), "10% of max per second");
            Assert.That(repair.State, Is.EqualTo(RepairPhase.Repairing));
        }

        [Test]
        public void Releasing_StopsWithoutALock()
        {
            var repair = new ChopperRepair();
            var health = new Health { Current = 50f, Max = 100f };
            Repair.Step(ref repair, Config, ref health, held: true, 0.5f);
            Repair.Step(ref repair, Config, ref health, held: false, 0.5f);

            Assert.That(repair.State, Is.EqualTo(RepairPhase.Idle));
            Assert.That(health.Current, Is.EqualTo(55f).Within(1e-3f));
            Repair.Step(ref repair, Config, ref health, held: true, 0.5f);
            Assert.That(health.Current, Is.EqualTo(60f).Within(1e-3f), "can start again at once");
        }

        [Test]
        public void AHit_StopsTheRepair_AndLocksItForTheCooldown()
        {
            var repair = new ChopperRepair();
            var health = new Health { Current = 50f, Max = 100f };
            Repair.Step(ref repair, Config, ref health, held: true, 0.1f);

            Repair.Interrupt(ref repair, Config);
            Assert.That(repair.State, Is.EqualTo(RepairPhase.Locked));
            var locked = health.Current;

            // Still held, but locked for 5 s: no healing.
            for (var i = 0; i < 49; i++) Repair.Step(ref repair, Config, ref health, held: true, 0.1f);
            Assert.That(health.Current, Is.EqualTo(locked));
            Assert.That(repair.State, Is.EqualTo(RepairPhase.Locked));

            // The lock ends and a key still held starts again.
            Repair.Step(ref repair, Config, ref health, held: true, 0.2f);
            Assert.That(repair.State, Is.EqualTo(RepairPhase.Repairing));
            Assert.That(health.Current, Is.GreaterThan(locked));
        }

        [Test]
        public void AHit_WhenNotRepairing_AlsoLocks_ForFiveSeconds()
        {
            var repair = new ChopperRepair();
            var health = new Health { Current = 50f, Max = 100f };
            Repair.Interrupt(ref repair, Config);
            Assert.That(repair.State, Is.EqualTo(RepairPhase.Locked));
            Assert.That(repair.LockLeft, Is.EqualTo(5f));

            Repair.Step(ref repair, Config, ref health, held: true, 4.9f);
            Assert.That(health.Current, Is.EqualTo(50f), "the key does nothing yet");
            Repair.Step(ref repair, Config, ref health, held: true, 0.2f);
            Assert.That(health.Current, Is.GreaterThan(50f), "ready after 5 s");
        }

        [Test]
        public void ANewHit_StartsTheLockAgain()
        {
            var repair = new ChopperRepair();
            var health = new Health { Current = 50f, Max = 100f };
            Repair.Interrupt(ref repair, Config);
            Repair.Step(ref repair, Config, ref health, held: false, 4f);
            Assert.That(repair.LockLeft, Is.EqualTo(1f).Within(1e-4f));

            Repair.Interrupt(ref repair, Config);
            Assert.That(repair.LockLeft, Is.EqualTo(5f));
            Repair.Step(ref repair, Config, ref health, held: false, 4f);
            Assert.That(repair.State, Is.EqualTo(RepairPhase.Locked));
        }

        [Test]
        public void DeadOrFull_DoesNotRepair()
        {
            var repair = new ChopperRepair();
            var dead = new Health { Current = 0f, Max = 100f };
            Assert.That(Repair.Step(ref repair, Config, ref dead, held: true, 1f), Is.EqualTo(0f));
            var full = new Health { Current = 100f, Max = 100f };
            Assert.That(Repair.Step(ref repair, Config, ref full, held: true, 1f), Is.EqualTo(0f));
            Assert.That(repair.State, Is.EqualTo(RepairPhase.Idle));
        }

        [Test]
        public void Hud_ShowsHint_Progress_AndLock()
        {
            var model = new ChopperHudModel();
            model.UpdateRepair(RepairPhase.Idle, 0f, 0.9f);
            Assert.That(model.Repair, Is.EqualTo(HudRepair.Hidden));

            model.UpdateRepair(RepairPhase.Idle, 0f, 0.5f);
            Assert.That(model.Repair, Is.EqualTo(HudRepair.Hint));
            Assert.That(model.RepairText, Is.EqualTo(ChopperHudModel.RepairHint));

            model.UpdateRepair(RepairPhase.Repairing, 0f, 0.634f);
            Assert.That(model.Repair, Is.EqualTo(HudRepair.Active));
            Assert.That(model.RepairText, Is.EqualTo("REPAIRING 63%"));

            model.UpdateRepair(RepairPhase.Locked, 2.41f, 0.6f);
            Assert.That(model.Repair, Is.EqualTo(HudRepair.Locked));
            Assert.That(model.RepairText, Is.EqualTo("REPAIR LOCKED 2.5"));

            model.UpdateRepair(RepairPhase.Locked, 1f, 0.6f, lockSeconds: 5f);
            Assert.That(model.RepairLockProgress, Is.EqualTo(0.8f).Within(1e-4f), "the bar fills as the lock runs out");
            model.UpdateRepair(RepairPhase.Idle, 0f, 0.6f, lockSeconds: 5f);
            Assert.That(model.RepairLockProgress, Is.EqualTo(0f));
        }

        [Test]
        public void SceneLabels_ReadAsWords()
        {
            Assert.That(SceneFlow.Pretty("Map_City04"), Is.EqualTo("MAP CITY04"));
            Assert.That(SceneFlow.Pretty("TrainingRange"), Is.EqualTo("TRAINING RANGE"));
            Assert.That(SceneFlow.Pretty("Hangar"), Is.EqualTo("HANGAR"));
        }
    }
}
