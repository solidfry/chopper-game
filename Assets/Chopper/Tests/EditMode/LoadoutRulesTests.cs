using System.Collections.Generic;
using Chopper.Simulation.Loadout;
using NUnit.Framework;

namespace Chopper.Tests
{
    public sealed class LoadoutRulesTests
    {
        private const ushort Scout = 1;
        private const ushort Assault = 3;
        private const ushort Minigun = 101;
        private const ushort Rockets = 111;
        private const ushort Turret = 121;
        private const ushort Flares = 131;
        private const ushort AgileRotor = 152;
        private const ushort Engine = 161;
        private const ushort HeavyArmour = 173;

        private sealed class Catalog : ILoadoutCatalog
        {
            private readonly Dictionary<ushort, ChassisSpec> _chassis = new();
            private readonly Dictionary<ushort, ModuleSpec> _modules = new();

            public Catalog Add(ChassisSpec chassis) { _chassis[chassis.Id] = chassis; return this; }
            public Catalog Add(ModuleSpec module) { _modules[module.Id] = module; return this; }
            public bool TryGetChassis(ushort id, out ChassisSpec chassis) => _chassis.TryGetValue(id, out chassis);
            public bool TryGetModule(ushort id, out ModuleSpec module) => _modules.TryGetValue(id, out module);
        }

        private sealed class Unlocks : IPlayerUnlocks
        {
            private readonly HashSet<ushort> _owned;
            public Unlocks(int level, params ushort[] owned) { Level = level; _owned = new HashSet<ushort>(owned); }
            public int Level { get; }
            public bool Owns(ushort moduleId) => _owned.Contains(moduleId);
        }

        private static readonly SlotType[] ScoutSlots =
            { SlotType.Nose, SlotType.Pylon, SlotType.Utility, SlotType.Rotor, SlotType.Engine, SlotType.Armour };

        private static Catalog MakeCatalog() => new Catalog()
            .Add(new ChassisSpec(Scout, "Scout", baseHealth: 70f, weightBudget: 60f, ScoutSlots))
            .Add(new ChassisSpec(Assault, "Assault", 150f, 130f, new[] { SlotType.Nose, SlotType.Turret }, unlockLevel: 4))
            .Add(new ModuleSpec(Minigun, "Minigun", SlotType.Nose, Stats(8f)))
            .Add(new ModuleSpec(Rockets, "Rockets", SlotType.Pylon, Stats(10f)))
            .Add(new ModuleSpec(Turret, "Turret", SlotType.Turret, Stats(20f), allowedChassis: new[] { Assault }))
            .Add(new ModuleSpec(Flares, "Flares", SlotType.Utility, Stats(4f)))
            .Add(new ModuleSpec(AgileRotor, "Agile", SlotType.Rotor, Stats(10f, turn: 1.2f), unlockLevel: 4))
            .Add(new ModuleSpec(Engine, "Engine", SlotType.Engine, Stats(12f)))
            .Add(new ModuleSpec(HeavyArmour, "Heavy", SlotType.Armour, Stats(24f, health: 60f)));

        private static ModuleStats Stats(float weight, float health = 0f, float turn = 1f) => new()
        {
            Weight = weight,
            HealthBonus = health,
            LiftMultiplier = 1f,
            TurnRateMultiplier = turn,
            BoostRechargeMultiplier = 1f,
        };

        private static readonly Unlocks Everything = new(99, Minigun, Rockets, Turret, Flares, AgileRotor, Engine, HeavyArmour);

        private static LoadoutSelection ScoutWith(ushort rotor, ushort armour) =>
            new(Scout, new[] { Minigun, Rockets, Flares, rotor, Engine, armour });

        [Test]
        public void Validate_AcceptsALegalLoadout()
        {
            var result = LoadoutRules.Validate(ScoutWith(AgileRotor, LoadoutSelection.EmptySlot), MakeCatalog(), Everything);
            Assert.That(result.IsValid, Is.True, result.ToString());
        }

        [Test]
        public void Validate_RejectsOverWeight()
        {
            // 8 + 10 + 4 + 10 + 12 + 24 = 68 > 60
            var result = LoadoutRules.Validate(ScoutWith(AgileRotor, HeavyArmour), MakeCatalog(), Everything);
            Assert.That(result.Error, Is.EqualTo(LoadoutError.OverWeight));
        }

        [Test]
        public void Validate_RejectsModuleInWrongSlot()
        {
            var selection = new LoadoutSelection(Scout, new[] { Rockets, Minigun, Flares, AgileRotor, Engine, LoadoutSelection.EmptySlot });
            var result = LoadoutRules.Validate(selection, MakeCatalog(), Everything);
            Assert.That(result.Error, Is.EqualTo(LoadoutError.WrongSlotType));
            Assert.That(result.Slot, Is.EqualTo(0));
        }

        [Test]
        public void Validate_RejectsModuleForAnotherChassis()
        {
            var catalog = MakeCatalog().Add(new ChassisSpec(9, "Test", 100f, 100f, new[] { SlotType.Turret }));
            var result = LoadoutRules.Validate(new LoadoutSelection(9, new[] { Turret }), catalog, Everything);
            Assert.That(result.Error, Is.EqualTo(LoadoutError.WrongChassis));
        }

        [Test]
        public void Validate_RejectsLockedAndUnownedModules()
        {
            var lowLevel = new Unlocks(1, Minigun, Rockets, Flares, AgileRotor, Engine);
            Assert.That(LoadoutRules.Validate(ScoutWith(AgileRotor, 0), MakeCatalog(), lowLevel).Error, Is.EqualTo(LoadoutError.ModuleLocked));

            var notOwned = new Unlocks(99, Minigun, Rockets, Flares, Engine);
            Assert.That(LoadoutRules.Validate(ScoutWith(AgileRotor, 0), MakeCatalog(), notOwned).Error, Is.EqualTo(LoadoutError.NotOwned));
        }

        [Test]
        public void Validate_RejectsBadIdsAndSlotCounts()
        {
            var catalog = MakeCatalog();
            Assert.That(LoadoutRules.Validate(new LoadoutSelection(77, new ushort[0]), catalog, Everything).Error,
                Is.EqualTo(LoadoutError.UnknownChassis));
            Assert.That(LoadoutRules.Validate(new LoadoutSelection(Scout, new[] { Minigun }), catalog, Everything).Error,
                Is.EqualTo(LoadoutError.WrongSlotCount));
            Assert.That(LoadoutRules.Validate(ScoutWith(999, 0), catalog, Everything).Error, Is.EqualTo(LoadoutError.UnknownModule));
            Assert.That(LoadoutRules.Validate(new LoadoutSelection(Assault, new ushort[] { 0, 0 }), catalog, new Unlocks(1)).Error,
                Is.EqualTo(LoadoutError.ChassisLocked));
        }

        [Test]
        public void Compute_AddsHealthAndAppliesWeightPenalty()
        {
            var catalog = MakeCatalog();
            catalog.TryGetChassis(Scout, out var scout);
            // Weight 8 + 10 + 4 + 10 + 12 = 44 of 60.
            var stats = LoadoutRules.Compute(scout, ScoutWith(AgileRotor, LoadoutSelection.EmptySlot), catalog);

            Assert.That(stats.Health, Is.EqualTo(70f));
            Assert.That(stats.Weight, Is.EqualTo(44f));
            Assert.That(stats.WeightFraction, Is.EqualTo(44f / 60f).Within(1e-5f));
            var expectedTurn = 1.2f * (1f - LoadoutRules.FullWeightTurnPenalty * 44f / 60f);
            Assert.That(stats.TurnRateMultiplier, Is.EqualTo(expectedTurn).Within(1e-5f));
        }
    }
}
