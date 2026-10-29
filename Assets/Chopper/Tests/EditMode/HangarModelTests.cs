using System.Collections.Generic;
using Chopper.Presentation.Hangar;
using Chopper.Simulation.Flight;
using Chopper.Simulation.Loadout;
using NUnit.Framework;
using UnityEngine;

namespace Chopper.Tests
{
    public sealed class HangarModelTests
    {
        private readonly List<Object> _created = new();
        private LoadoutCatalog _catalog;
        private ModuleDefinition _gun;
        private ModuleDefinition _heavyGun;
        private ModuleDefinition _laser;
        private ModuleDefinition _rotor;

        private sealed class Unlocks : IPlayerUnlocks
        {
            private readonly int _level;
            public Unlocks(int level) => _level = level;
            public int Level => _level;
            public bool Owns(ushort moduleId) => true;
        }

        [SetUp]
        public void SetUp()
        {
            _gun = Module(10, "Gun", SlotType.Nose, weight: 8f, level: 0);
            _heavyGun = Module(11, "Heavy Gun", SlotType.Nose, weight: 50f, level: 0);
            _laser = Module(12, "Laser", SlotType.Nose, weight: 6f, level: 20);
            _rotor = Module(20, "Rotor", SlotType.Rotor, weight: 10f, level: 0, turn: 1.2f);

            var scout = Chassis(1, "Scout", budget: 40f, new[] { SlotType.Nose, SlotType.Rotor }, new[] { _gun, _rotor });
            var gunship = Chassis(2, "Gunship", budget: 90f, new[] { SlotType.Nose, SlotType.Rotor, SlotType.Rotor }, new[] { _gun, _rotor, _rotor });
            _catalog = ScriptableObject.CreateInstance<LoadoutCatalog>();
            _created.Add(_catalog);
            _catalog.Initialise(new[] { scout, gunship }, new[] { _gun, _heavyGun, _laser, _rotor });
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in _created) Object.DestroyImmediate(asset);
            _created.Clear();
        }

        [Test]
        public void New_StartsOnTheFirstChassisWithItsDefaults()
        {
            var model = new HangarModel(_catalog, null, null, null);

            Assert.That(model.ChassisNames, Is.EqualTo(new[] { "SCOUT", "GUNSHIP" }));
            Assert.That(model.Fitted, Is.EqualTo(new[] { _gun, _rotor }));
            Assert.That(model.FocusedSlot, Is.EqualTo(HangarModel.Overview));
            Assert.That(model.CanLaunch, Is.True);
        }

        [Test]
        public void Slots_NumberRepeatedTypes()
        {
            var model = new HangarModel(_catalog, null, null, null);
            model.SelectChassis(1);

            Assert.That(model.Slots[0].Label, Is.EqualTo("NOSE"));
            Assert.That(model.Slots[1].Label, Is.EqualTo("ROTOR 1"));
            Assert.That(model.Slots[2].Label, Is.EqualTo("ROTOR 2"));
        }

        [Test]
        public void FocusSlot_ListsEmptyThenModulesOfThatSlotByLevel()
        {
            var model = new HangarModel(_catalog, null, null, new Unlocks(0));
            model.FocusSlot(0);

            Assert.That(model.Options.Count, Is.EqualTo(4));
            Assert.That(model.Options[0].Name, Is.EqualTo("EMPTY"));
            Assert.That(model.Options[1].Name, Is.EqualTo("GUN"));
            Assert.That(model.Options[1].Fitted, Is.True);
            Assert.That(model.Options[3].Name, Is.EqualTo("LASER"));
            Assert.That(model.Options[3].Locked, Is.True);
            Assert.That(model.Options[3].Detail, Is.EqualTo("LEVEL 20"));
        }

        [Test]
        public void Fit_LockedModuleDoesNothing()
        {
            var model = new HangarModel(_catalog, null, null, new Unlocks(0));
            model.FocusSlot(0);
            model.Fit(3);

            Assert.That(model.Fitted[0], Is.EqualTo(_gun));
        }

        [Test]
        public void Fit_OverBudgetBlocksLaunch()
        {
            var model = new HangarModel(_catalog, null, null, null);
            model.FocusSlot(0);
            model.Fit(2); // Heavy Gun: 50 + rotor 10 > Scout budget 40.

            Assert.That(model.OverWeight, Is.True);
            Assert.That(model.CanLaunch, Is.False);
            Assert.That(model.Launch(), Is.False);
        }

        [Test]
        public void Fit_EmptySlotIsAllowed()
        {
            var model = new HangarModel(_catalog, null, null, null);
            model.FocusSlot(0);
            model.Fit(0);

            Assert.That(model.Fitted[0], Is.Null);
            Assert.That(model.Slots[0].Empty, Is.True);
            Assert.That(model.CanLaunch, Is.True);
        }

        [Test]
        public void SelectChassis_EachChassisKeepsItsOwnModules()
        {
            var model = new HangarModel(_catalog, null, null, null);
            model.FocusSlot(0);
            model.Fit(0);
            model.SelectChassis(1);
            Assert.That(model.Fitted[0], Is.EqualTo(_gun));
            Assert.That(model.FocusedSlot, Is.EqualTo(HangarModel.Overview));

            model.SelectChassis(0);
            Assert.That(model.Fitted[0], Is.Null);
        }

        [Test]
        public void Hover_PreviewsStatsWithoutChangingTheBuild()
        {
            var model = new HangarModel(_catalog, null, null, null);
            model.FocusSlot(1);
            var changed = 0;
            var statsChanged = 0;
            model.Changed += () => changed++;
            model.StatsChanged += () => statsChanged++;

            model.Hover(0); // Empty rotor slot: lighter, turns slower (no x1.2).
            var turn = model.Stats[2];

            Assert.That(turn.Label, Is.EqualTo("TURN"));
            Assert.That(turn.PreviewFraction, Is.LessThan(turn.Fraction));
            Assert.That(model.PreviewWeightFraction, Is.LessThan(model.WeightFraction));
            Assert.That(model.Fitted[1], Is.EqualTo(_rotor));
            Assert.That(changed, Is.EqualTo(0));
            Assert.That(statsChanged, Is.EqualTo(1));
        }

        [Test]
        public void Back_ReturnsToOverviewOnce()
        {
            var model = new HangarModel(_catalog, null, null, null);
            model.FocusSlot(1);

            Assert.That(model.Back(), Is.True);
            Assert.That(model.FocusedSlot, Is.EqualTo(HangarModel.Overview));
            Assert.That(model.Back(), Is.False);
        }

        [Test]
        public void Launch_ValidRaisesLaunched()
        {
            var model = new HangarModel(_catalog, null, null, null);
            var launched = false;
            model.Launched += () => launched = true;

            Assert.That(model.Launch(), Is.True);
            Assert.That(launched, Is.True);
        }

        [Test]
        public void ChoiceText_RoundTrips()
        {
            var selection = new LoadoutSelection(2, new ushort[] { 10, 0, 20 });

            var text = LoadoutChoiceStore.Encode(selection);
            Assert.That(text, Is.EqualTo("2:10,0,20"));
            Assert.That(LoadoutChoiceStore.TryDecode(text, out var decoded), Is.True);
            Assert.That(decoded.ChassisId, Is.EqualTo(2));
            Assert.That(decoded.Modules, Is.EqualTo(new ushort[] { 10, 0, 20 }));
        }

        [TestCase("")]
        [TestCase("0:1,2")]
        [TestCase("2")]
        [TestCase("2:1,x")]
        [TestCase("2:1,2,3,4,5,6,7,8,9")]
        public void ChoiceText_RejectsBadText(string text)
        {
            Assert.That(LoadoutChoiceStore.TryDecode(text, out _), Is.False);
        }

        [Test]
        public void Wireframe_CubeHasTwelveEdgesAndNoDiagonals()
        {
            // A cube made like a flat-shaded export: 4 vertices per face, 2 triangles per face.
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var normals = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var normal in normals)
            {
                var u = Vector3.Cross(normal, Mathf.Abs(normal.y) > 0.5f ? Vector3.forward : Vector3.up);
                var v = Vector3.Cross(normal, u);
                var start = vertices.Count;
                vertices.Add((normal - u - v) * 0.5f);
                vertices.Add((normal + u - v) * 0.5f);
                vertices.Add((normal + u + v) * 0.5f);
                vertices.Add((normal - u + v) * 0.5f);
                triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }

            var (points, lines) = WireframeMesh.Build(vertices, triangles, creaseDegrees: 30f, push: 0f);

            Assert.That(lines.Length / 2, Is.EqualTo(12));
            Assert.That(points.Length, Is.EqualTo(24));
        }

        private ModuleDefinition Module(ushort id, string name, SlotType slot, float weight, int level, float turn = 1f)
        {
            var module = ScriptableObject.CreateInstance<ModuleDefinition>();
            _created.Add(module);
            var stats = ModuleStats.None;
            stats.Weight = weight;
            stats.TurnRateMultiplier = turn;
            module.Initialise(id, name, "", slot, stats, level);
            return module;
        }

        private ChassisDefinition Chassis(ushort id, string name, float budget, SlotType[] slots, ModuleDefinition[] defaults)
        {
            var chassis = ScriptableObject.CreateInstance<ChassisDefinition>();
            _created.Add(chassis);
            chassis.Initialise(id, name, "", 100f, budget, 0, slots, new FlightTuning());
            chassis.SetDefaults(null, defaults);
            return chassis;
        }
    }
}
