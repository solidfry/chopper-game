using Chopper.Simulation.Flight;
using Chopper.Simulation.Loadout;
using NUnit.Framework;

namespace Chopper.Tests
{
    public sealed class FlightEstimateTests
    {
        private static LoadoutStats Stats(float speed = 1f, float boostPower = 1f) =>
            new(health: 100f, weight: 0f, weightBudget: 90f, lift: 1f, turnRate: 1f, boostRecharge: 1f, speed, boostPower);

        [Test]
        public void SpeedMultiplier_ScalesTopSpeedExactly()
        {
            var flight = new FlightTuning();

            var normal = FlightEstimate.TopSpeed(flight, Stats());
            var fast = FlightEstimate.TopSpeed(flight, Stats(speed: 1.2f));

            Assert.That(normal, Is.GreaterThan(10f));
            Assert.That(fast / normal, Is.EqualTo(1.2f).Within(1e-3f));
        }

        [Test]
        public void BoostPower_ScalesBoostAcceleration()
        {
            var flight = new FlightTuning();

            Assert.That(FlightEstimate.BoostPower(flight, Stats(boostPower: 1.5f)), Is.EqualTo(flight.BoostAcceleration * 1.5f).Within(1e-4f));
        }

        [Test]
        public void Compute_ModuleWithoutTheNewStatsChangesNothing()
        {
            // A module saved before speed and boost power existed has 0 there.
            var oldStats = new ModuleStats { Weight = 0f, LiftMultiplier = 1f, TurnRateMultiplier = 1f, BoostRechargeMultiplier = 1f };
            var chassis = new ChassisSpec(1, "Test", 100f, 50f, new[] { SlotType.Engine });
            var catalog = new SingleModule(new ModuleSpec(10, "Old", SlotType.Engine, oldStats));

            var stats = LoadoutRules.Compute(chassis, new LoadoutSelection(1, new ushort[] { 10 }), catalog);

            Assert.That(stats.SpeedMultiplier, Is.EqualTo(1f));
            Assert.That(stats.BoostPowerMultiplier, Is.EqualTo(1f));
        }

        [Test]
        public void Compute_FullWeightLowersTopSpeed()
        {
            var heavy = new ModuleStats { Weight = 50f, LiftMultiplier = 1f, TurnRateMultiplier = 1f, BoostRechargeMultiplier = 1f, SpeedMultiplier = 1f, BoostPowerMultiplier = 1f };
            var chassis = new ChassisSpec(1, "Test", 100f, 50f, new[] { SlotType.Armour });
            var catalog = new SingleModule(new ModuleSpec(10, "Heavy", SlotType.Armour, heavy));

            var stats = LoadoutRules.Compute(chassis, new LoadoutSelection(1, new ushort[] { 10 }), catalog);

            Assert.That(stats.SpeedMultiplier, Is.EqualTo(1f - LoadoutRules.FullWeightSpeedPenalty).Within(1e-5f));
        }

        private sealed class SingleModule : ILoadoutCatalog
        {
            private readonly ModuleSpec _module;
            public SingleModule(ModuleSpec module) => _module = module;
            public bool TryGetChassis(ushort id, out ChassisSpec chassis) { chassis = null; return false; }
            public bool TryGetModule(ushort id, out ModuleSpec module) { module = _module; return id == _module.Id; }
        }
    }
}
