using System.Collections.Generic;
using Chopper.Presentation.Profile;
using Chopper.Simulation.Flight;
using Chopper.Simulation.Loadout;
using NUnit.Framework;
using UnityEngine;

namespace Chopper.Tests
{
    public sealed class UnlocksModelTests
    {
        private sealed class FakePlayer : IPlayerUnlocks
        {
            public int Level { get; set; } = 1;
            public int Mastery = 1;
            public bool Owns(ushort moduleId) => false;
            public int ChassisLevel(ChassisClass chassisClass) => Mastery;
            public int PilotLevel(ushort pilotId) => 1;
        }

        private readonly List<Object> _created = new();
        private LoadoutCatalog _catalog;
        private FakePlayer _player;
        private ModuleDefinition _rotor1, _rotor2, _rotor3, _railgun;

        [SetUp]
        public void SetUp()
        {
            _rotor1 = Module(10, "Rotor Mk I", SlotType.Rotor, level: 0);
            _rotor2 = Module(11, "Rotor Mk II", SlotType.Rotor, level: 0);
            _rotor3 = Module(12, "Rotor Mk III", SlotType.Rotor, level: 5);
            _rotor2.SetChain(_rotor1, 3);
            _rotor3.SetChain(_rotor2, 6);
            _railgun = Module(20, "Railgun", SlotType.Nose, level: 12);
            var gun = Module(21, "Gun", SlotType.Nose, level: 0);

            var scout = ScriptableObject.CreateInstance<ChassisDefinition>();
            _created.Add(scout);
            scout.Initialise(1, "Scout", "", 100f, 60f, 0, new[] { SlotType.Nose, SlotType.Rotor }, new FlightTuning());
            scout.SetClass(ChassisClass.Scout);
            var lifter = ScriptableObject.CreateInstance<ChassisDefinition>();
            _created.Add(lifter);
            lifter.Initialise(2, "Lifter", "", 100f, 100f, 8, new[] { SlotType.Nose, SlotType.Rotor }, new FlightTuning());
            lifter.SetClass(ChassisClass.Lifter);

            _catalog = ScriptableObject.CreateInstance<LoadoutCatalog>();
            _created.Add(_catalog);
            _catalog.Initialise(new[] { scout, lifter }, new[] { _rotor1, _rotor2, _rotor3, _railgun, gun });
            _player = new FakePlayer();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void AccountLevels_GroupItemsByLevel_AndMarkTheNextOne()
        {
            _player.Level = 6;
            var levels = new UnlocksModel(_catalog, _player).AccountLevels();

            // Rotor Mk III (account level 5) is a chain tier: not on the account ladder.
            Assert.That(levels.ConvertAll(l => l.Level), Is.EqualTo(new[] { 1, 8, 12 }));
            Assert.That(levels[0].State, Is.EqualTo(UnlockState.Open));
            Assert.That(levels[1].State, Is.EqualTo(UnlockState.Next));
            Assert.That(levels[1].Items[0].Name, Is.EqualTo("LIFTER"));
            Assert.That(levels[2].State, Is.EqualTo(UnlockState.Locked));
            Assert.That(levels[2].Items[0].Reason, Is.EqualTo("LVL 12"));
        }

        [Test]
        public void AccountLevels_LeaveOutChainTiers()
        {
            var levels = new UnlocksModel(_catalog, _player).AccountLevels();
            foreach (var level in levels)
                Assert.That(level.Items, Has.None.Matches<UnlockItem>(i => i.Name.Contains("MK II")));
        }

        [Test]
        public void NextUnlockText_NamesTheNextLevel()
        {
            _player.Level = 9;
            Assert.That(new UnlocksModel(_catalog, _player).NextUnlockText(), Is.EqualTo("NEXT: RAILGUN AT LVL 12"));
            _player.Level = 50;
            Assert.That(new UnlocksModel(_catalog, _player).NextUnlockText(), Is.EqualTo(""));
        }

        [Test]
        public void Chains_FollowParents_AndGiveTheLockReason()
        {
            _player.Level = 1;
            _player.Mastery = 3;
            var chains = new UnlocksModel(_catalog, _player).Chains(ChassisClass.Scout);

            Assert.That(chains.Count, Is.EqualTo(1));
            var tiers = chains[0].Tiers;
            Assert.That(chains[0].Kind, Is.EqualTo("ROTOR"));
            Assert.That(tiers.Count, Is.EqualTo(3));
            Assert.That(tiers[0].State, Is.EqualTo(UnlockState.Open));
            Assert.That(tiers[1].State, Is.EqualTo(UnlockState.Open));
            Assert.That(tiers[2].State, Is.EqualTo(UnlockState.Next));
            Assert.That(tiers[2].Reason, Is.EqualTo("LVL 5"));
        }

        [Test]
        public void Chains_ALockedParentLocksTheTiersAfterIt()
        {
            _player.Level = 10;
            _player.Mastery = 1;
            var tiers = new UnlocksModel(_catalog, _player).Chains(ChassisClass.Scout)[0].Tiers;

            Assert.That(tiers[1].State, Is.EqualTo(UnlockState.Next));
            Assert.That(tiers[1].Reason, Is.EqualTo("MST 3"));
            Assert.That(tiers[2].State, Is.EqualTo(UnlockState.Locked));
            Assert.That(tiers[2].Reason, Is.EqualTo("NEEDS MK II"));
        }

        [Test]
        public void Chains_AClassOnlyTierShowsOnlyOnItsClass()
        {
            var lifter = _catalog.Chassis[1];
            var heavy = Module(30, "Heavy-Lift Rotor", SlotType.Rotor, level: 0);
            var heavy2 = ScriptableObject.CreateInstance<ModuleDefinition>();
            _created.Add(heavy2);
            heavy2.Initialise(31, "Heavy-Lift Rotor Mk II", "", SlotType.Rotor, ModuleStats.None, 0, new[] { lifter });
            heavy2.SetChain(heavy, 5);
            var modules = new List<ModuleDefinition>(_catalog.Modules) { heavy, heavy2 };
            _catalog.Initialise(new[] { _catalog.Chassis[0], lifter }, modules.ToArray());
            var model = new UnlocksModel(_catalog, _player);

            Assert.That(model.Chains(ChassisClass.Lifter).Exists(c => c.Tiers[0].Name == "HEAVY-LIFT ROTOR"), Is.True);
            Assert.That(model.Chains(ChassisClass.Scout).Exists(c => c.Tiers[0].Name == "HEAVY-LIFT ROTOR"), Is.False);
        }

        [Test]
        public void Chains_EmptyForAClassWithNoChassis()
        {
            Assert.That(new UnlocksModel(_catalog, _player).Chains(ChassisClass.Gunship), Is.Empty);
        }

        private ModuleDefinition Module(ushort id, string name, SlotType slot, int level)
        {
            var module = ScriptableObject.CreateInstance<ModuleDefinition>();
            _created.Add(module);
            module.Initialise(id, name, "", slot, ModuleStats.None, level);
            return module;
        }
    }
}
