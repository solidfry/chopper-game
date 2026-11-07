using System.Collections.Generic;
using Chopper.Presentation.Hangar;
using Chopper.Simulation.Combat;
using Chopper.Simulation.Flight;
using Chopper.Simulation.Loadout;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Chopper.Tests
{
    public sealed class PilotTests
    {
        private readonly List<Object> _created = new();

        private sealed class MemoryPrefs : IHangarPrefs
        {
            private readonly Dictionary<string, string> _strings = new();
            private readonly Dictionary<string, int> _ints = new();
            public string GetString(string key, string fallback) => _strings.TryGetValue(key, out var value) ? value : fallback;
            public int GetInt(string key, int fallback) => _ints.TryGetValue(key, out var value) ? value : fallback;
            public void SetString(string key, string value) => _strings[key] = value;
            public void SetInt(string key, int value) => _ints[key] = value;
            public void Save() { }
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in _created) Object.DestroyImmediate(asset);
            _created.Clear();
        }

        private PilotDefinition Pilot(ushort id, string sign, PilotModifiers modifiers, int level = 0)
        {
            var pilot = ScriptableObject.CreateInstance<PilotDefinition>();
            pilot.Initialise(id, sign, "ROLE", "Passive.", "ABILITY", "Text.", Color.green, modifiers, level);
            _created.Add(pilot);
            return pilot;
        }

        [Test]
        public void ApplyPilot_ScalesTheBuiltLoadout_AndSetsThePilotId()
        {
            var built = new BuiltLoadout
            {
                Health = 100f,
                Flight = new ChopperFlightConfig { MaxTurnRate = new float3(1f), CollectiveUp = 10f, BoostRechargeTime = 4f },
                Repair = RepairConfig.Default,
                Gun = new HitscanWeaponConfig { Damage = 10f },
            };
            built.Pylons.Add(new PylonWeapon { LockTime = 2f, MagazineSize = 8 });
            var pilot = Pilot(3, "TEST", new PilotModifiers
            {
                HealthMultiplier = 0.9f, RepairTimeMultiplier = 0.6f, RepairLockMultiplier = 0.5f, GunDamageMultiplier = 0.95f,
                LockTimeMultiplier = 0.7f, MagazineMultiplier = 0.75f, BoostRechargeMultiplier = 0.65f, EnemyLockTimeMultiplier = 1.4f,
                HeatMultiplier = 0.6f, DamageTakenMultiplier = 0.85f,
            });

            LoadoutBuilder.ApplyPilot(pilot, built);

            Assert.That(built.Code.PilotId, Is.EqualTo(3));
            Assert.That(built.Health, Is.EqualTo(90f).Within(1e-3f));
            Assert.That(built.Repair.Seconds, Is.EqualTo(6f).Within(1e-3f));
            Assert.That(built.Repair.InterruptCooldown, Is.EqualTo(1.5f).Within(1e-3f));
            Assert.That(built.Gun.Damage, Is.EqualTo(9.5f).Within(1e-3f));
            Assert.That(built.Flight.BoostRechargeTime, Is.EqualTo(2.6f).Within(1e-3f));
            Assert.That(built.Pylons[0].LockTime, Is.EqualTo(1.4f).Within(1e-3f));
            Assert.That(built.Pylons[0].MagazineSize, Is.EqualTo(6));
            Assert.That(built.Target.LockScale, Is.EqualTo(1.4f).Within(1e-3f));
            Assert.That(built.Target.BaseHeat, Is.EqualTo(0.6f).Within(1e-3f));
            Assert.That(built.Traits.DamageTakenScale, Is.EqualTo(0.85f).Within(1e-3f));
        }

        [Test]
        public void Effects_ReadAsSignedPercentages()
        {
            var effects = new PilotModifiers { RepairTimeMultiplier = 0.6f, RepairLockMultiplier = 0.5f, BoostRechargeMultiplier = 1.15f }.Effects();
            Assert.That(effects, Has.Member(("+40% REPAIR SPEED", true)));
            Assert.That(effects, Has.Member(("-50% REPAIR LOCK", true)));
            Assert.That(effects, Has.Member(("-15% BOOST RECHARGE", false)));
            Assert.That(new PilotModifiers().Effects(), Is.Empty, "no change = no lines");
        }

        [Test]
        public void Traits_ScaleDamageTaken_AndCrashesTwice()
        {
            var traits = PilotTraits.None;
            traits.DamageTakenScale = 0.5f;
            traits.CrashTakenScale = 0.5f;
            Assert.That(traits.Taken(10f, DamageKind.Bullet), Is.EqualTo(5f).Within(1e-4f));
            Assert.That(traits.Taken(10f, DamageKind.Impact), Is.EqualTo(2.5f).Within(1e-4f));
            Assert.That(PilotTraits.None.Taken(10f, DamageKind.Explosion), Is.EqualTo(10f));
        }

        [Test]
        public void GunHitsCorrode_BlastsBurn_AndDamageOverTimeNeverChains()
        {
            var traits = PilotTraits.None;
            traits.BulletDotShare = 0.3f;
            traits.ExplosiveDotShare = 0.25f;
            Assert.That(traits.DotFor(DamageKind.Bullet, 10f, out var corrosion), Is.True);
            Assert.That(corrosion.Kind, Is.EqualTo(DamageKind.Corrosion));
            Assert.That(corrosion.Remaining, Is.EqualTo(3f).Within(1e-4f));
            Assert.That(traits.DotFor(DamageKind.Explosion, 40f, out var burn), Is.True);
            Assert.That(burn.Kind, Is.EqualTo(DamageKind.Burn));
            Assert.That(burn.Remaining, Is.EqualTo(10f).Within(1e-4f));
            Assert.That(traits.DotFor(DamageKind.Corrosion, 1f, out _), Is.False);
            Assert.That(traits.DotFor(DamageKind.Impact, 10f, out _), Is.False);
            Assert.That(PilotTraits.None.DotFor(DamageKind.Bullet, 10f, out _), Is.False);
        }

        [Test]
        public void Pulses_DealAllTheDamage_ByTheEnd()
        {
            var effect = new DamageOverTime { Remaining = 3f, SecondsLeft = 3f, Kind = DamageKind.Corrosion };
            var total = 0f;
            for (var i = 0; i < 6; i++) total += DamageOverTimeRules.Pulse(ref effect, DamageOverTimeRules.PulseSeconds);
            Assert.That(total, Is.EqualTo(3f).Within(1e-4f));
            Assert.That(effect.SecondsLeft, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(DamageOverTimeRules.Pulse(ref effect, 0.5f), Is.EqualTo(0f));
        }

        [Test]
        public void SameSourceAndKind_Merges_OthersStack()
        {
            using var world = new World("PilotTests");
            var entity = world.EntityManager.CreateEntity();
            var buffer = world.EntityManager.AddBuffer<DamageOverTime>(entity);
            var shooter = world.EntityManager.CreateEntity();
            var other = world.EntityManager.CreateEntity();

            DamageOverTimeRules.Add(buffer, new DamageOverTime { Remaining = 3f, SecondsLeft = 3f, Source = shooter, Kind = DamageKind.Corrosion });
            DamageOverTimeRules.Add(buffer, new DamageOverTime { Remaining = 3f, SecondsLeft = 3f, Source = shooter, Kind = DamageKind.Corrosion });
            DamageOverTimeRules.Add(buffer, new DamageOverTime { Remaining = 5f, SecondsLeft = 4f, Source = other, Kind = DamageKind.Burn });

            Assert.That(buffer.Length, Is.EqualTo(2));
            Assert.That(buffer[0].Remaining, Is.EqualTo(6f).Within(1e-4f));
        }

        [Test]
        public void LoadoutCode_CarriesThePilot()
        {
            var a = LoadoutCode.From(new LoadoutSelection(1, new ushort[] { 101 }), pilotId: 4);
            var b = a;
            b.PilotId = 5;
            Assert.That(a.Equals(b), Is.False, "another pilot is another loadout (it is applied again)");
            Assert.That(ChopperLoadout.From(a).Code.PilotId, Is.EqualTo(4));
        }

        [Test]
        public void SelectScreen_ShowsLocksInAndSaves()
        {
            var prefs = new MemoryPrefs();
            var store = new LoadoutChoiceStore(prefs);
            var pilots = new[] { Pilot(1, "APOTHECARY", new PilotModifiers()), Pilot(2, "VIPER", new PilotModifiers()), Pilot(3, "LOCKED", new PilotModifiers(), level: 5) };
            var model = new PilotSelectModel(pilots, store, new LevelUnlocks(1));

            Assert.That(model.SelectedIndex, Is.EqualTo(-1), "nothing saved: no pilot");
            model.Open();
            Assert.That(model.IsOpen, Is.True);
            model.Show(1);
            Assert.That(model.LockIn(), Is.True);
            Assert.That(model.IsOpen, Is.False);
            Assert.That(store.Pilot, Is.EqualTo(2));

            model.Open();
            model.Show(2);
            Assert.That(model.Cards[2].Locked, Is.True);
            Assert.That(model.LockIn(), Is.False, "a locked pilot cannot be picked");
            Assert.That(store.Pilot, Is.EqualTo(2));

            var next = new PilotSelectModel(pilots, new LoadoutChoiceStore(prefs), new LevelUnlocks(1));
            Assert.That(next.Selected?.CallSign, Is.EqualTo("VIPER"), "the pick is kept across sessions");
        }

        private sealed class LevelUnlocks : IPlayerUnlocks
        {
            public LevelUnlocks(int level) => Level = level;
            public int Level { get; }
            public bool Owns(ushort moduleId) => true;
        }
    }
}
