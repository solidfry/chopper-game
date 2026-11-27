using System.Collections.Generic;
using Chopper.Presentation.Effects;
using Chopper.Simulation.Definitions;
using Chopper.Simulation.Loadout;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;

namespace Chopper.Tests.EditMode
{
    /// <summary>Weapon look plumbing (Docs/WeaponFeelPlan.md step 1): module id -> family -> kind default.</summary>
    public sealed class WeaponLookTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in _created) Object.DestroyImmediate(asset);
            _created.Clear();
        }

        private T Make<T>() where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            _created.Add(asset);
            return asset;
        }

        private ModuleDefinition Module(ushort id, SlotType slot, WeaponDefinition weapon)
        {
            var module = Make<ModuleDefinition>();
            module.Initialise(id, $"Module {id}", "", slot, ModuleStats.None, 0);
            module.SetWeapon(weapon);
            return module;
        }

        private WeaponLookLibrary Library(out WeaponLook gun, out WeaponLook rocket, out WeaponLook guided)
        {
            gun = Make<WeaponLook>();
            rocket = Make<WeaponLook>();
            guided = Make<WeaponLook>();
            var library = Make<WeaponLookLibrary>();
            library.Initialise(gun, rocket, guided);
            return library;
        }

        [Test]
        public void For_ModuleInAFamily_GetsTheFamilyLook()
        {
            var library = Library(out _, out _, out _);
            var minigun = Make<WeaponLook>();
            var mk1 = Module(11, SlotType.Nose, Make<HitscanWeaponDefinition>());
            var mk2 = Module(12, SlotType.Nose, Make<HitscanWeaponDefinition>());
            library.SetFamilies(new[] { new WeaponLookLibrary.Family("Minigun", minigun, new[] { mk1, mk2 }) });

            Assert.That(library.For(11, WeaponKind.Gun), Is.SameAs(minigun));
            Assert.That(library.For(12, WeaponKind.Gun), Is.SameAs(minigun), "Mk tiers share the family look.");
        }

        [Test]
        public void For_UnknownOrZeroModule_GetsTheKindDefault()
        {
            var library = Library(out var gun, out var rocket, out var guided);

            Assert.That(library.For(99, WeaponKind.Gun), Is.SameAs(gun));
            Assert.That(library.For(0, WeaponKind.Gun), Is.SameAs(gun), "Turrets (id 0) use the gun default.");
            Assert.That(library.For(0, WeaponKind.Rocket), Is.SameAs(rocket));
            Assert.That(library.For(0, WeaponKind.Guided), Is.SameAs(guided));
            Assert.That(library.For(0, WeaponKind.Bomb), Is.SameAs(rocket), "No bomb default: the rocket one.");
            Assert.That(library.For(0, WeaponKind.Mine), Is.SameAs(rocket), "No mine default: the rocket one.");
        }

        [Test]
        public void RocketKind_FromThePath()
        {
            Assert.That(WeaponLookLibrary.RocketKind(0f, 0f), Is.EqualTo(WeaponKind.Rocket));
            Assert.That(WeaponLookLibrary.RocketKind(9.8f, 0f), Is.EqualTo(WeaponKind.Bomb));
            Assert.That(WeaponLookLibrary.RocketKind(0f, 1.5f), Is.EqualTo(WeaponKind.Mine));
        }

        [Test]
        public void CollectProblems_NamesMissingDefaultsAndDoubleModules()
        {
            var library = Make<WeaponLookLibrary>();
            library.name = "Looks";
            var gun = Module(5, SlotType.Nose, Make<HitscanWeaponDefinition>());
            gun.name = "Gun";
            var bare = Module(6, SlotType.Nose, null);
            bare.name = "Bare";
            library.SetFamilies(new[]
            {
                new WeaponLookLibrary.Family("A", Make<WeaponLook>(), new[] { gun }),
                new WeaponLookLibrary.Family("B", null, new[] { gun, bare }),
            });

            var problems = new List<string>();
            library.CollectProblems(problems);

            Assert.That(problems, Has.Some.Contains("no gun default"));
            Assert.That(problems, Has.Some.Contains("no rocket default"));
            Assert.That(problems, Has.Some.Contains("no guided missile default"));
            Assert.That(problems, Has.Some.Contains("B has no look"));
            Assert.That(problems, Has.Some.Contains("Gun is in A and B"));
            Assert.That(problems, Has.Some.Contains("Bare, which has no weapon"));
        }

        [Test]
        public void CollectProblems_CleanLibrary_HasNone()
        {
            var library = Library(out _, out _, out _);
            library.SetFamilies(new[]
            {
                new WeaponLookLibrary.Family("Minigun", Make<WeaponLook>(),
                    new[] { Module(11, SlotType.Nose, Make<HitscanWeaponDefinition>()) }),
            });
            var problems = new List<string>();
            library.CollectProblems(problems);
            Assert.That(problems, Is.Empty);
        }

        [Test]
        public void Settings_PickTheLookSlot_ElseTheSharedOne()
        {
            var shared = new GameObject("shared");
            var own = new GameObject("own");
            _created.Add(shared);
            _created.Add(own);
            Assert.That(CombatEffectsSettings.Pick(own, shared), Is.SameAs(own));
            Assert.That(CombatEffectsSettings.Pick(null, shared), Is.SameAs(shared));
            var settings = Make<CombatEffectsSettings>();
            Assert.That(settings.Look(11, WeaponKind.Gun), Is.Null, "No library: shared prefabs only.");
        }

        [Test]
        public void Build_CarriesTheModuleIds_OnTheGunAndPylons()
        {
            var gun = Module(21, SlotType.Nose, Make<HitscanWeaponDefinition>());
            var pod = Module(22, SlotType.Pylon, Make<RocketWeaponDefinition>());
            var built = LoadoutBuilder.Build(null, new[] { gun, pod }, null, _ => Entity.Null);

            Assert.That(built.Gun.ModuleId, Is.EqualTo(21));
            Assert.That(built.Pylons.Count, Is.EqualTo(1));
            Assert.That(built.Pylons[0].ModuleId, Is.EqualTo(22));
        }
    }
}
