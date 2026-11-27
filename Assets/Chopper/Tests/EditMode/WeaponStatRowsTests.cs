using System.Collections.Generic;
using Chopper.Presentation.Hangar;
using Chopper.Simulation.Definitions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Chopper.Tests.EditMode
{
    /// <summary>Hangar weapon numbers (Docs/WeaponFeelPlan.md step 5).</summary>
    public sealed class WeaponStatRowsTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in _created) Object.DestroyImmediate(asset);
            _created.Clear();
        }

        private T Make<T>(params (string Field, float Value)[] fields) where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            _created.Add(asset);
            var serialized = new SerializedObject(asset);
            foreach (var (field, value) in fields)
            {
                var property = serialized.FindProperty(field);
                Assert.That(property, Is.Not.Null, field);
                if (property.propertyType == SerializedPropertyType.Integer) property.intValue = (int)value;
                else property.floatValue = value;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return asset;
        }

        private static HangarStatRow Row(List<HangarStatRow> rows, string label) => rows.Find(r => r.Label == label);

        [Test]
        public void Gun_ShowsDamageRateRangeAndHeat_AgainstTheBestInTheSlot()
        {
            var minigun = Make<HitscanWeaponDefinition>(("roundsPerMinute", 750f), ("damagePerShot", 8f), ("range", 600f), ("heatSeconds", 6f));
            var cannon = Make<HitscanWeaponDefinition>(("roundsPerMinute", 150f), ("damagePerShot", 40f), ("range", 800f), ("heatSeconds", 0f));
            var rows = new List<HangarStatRow>();

            WeaponStatRows.Build(cannon, minigun, new WeaponDefinition[] { minigun, cannon }, rows);

            Assert.That(Row(rows, "DAMAGE").Value, Is.EqualTo("40"));
            Assert.That(Row(rows, "DAMAGE").PreviewFraction, Is.EqualTo(1f), "Best damage in the slot.");
            Assert.That(Row(rows, "DAMAGE").Fraction, Is.EqualTo(0.2f).Within(1e-4f), "The fitted minigun: 8 of 40.");
            Assert.That(Row(rows, "RATE").Value, Is.EqualTo("150"));
            Assert.That(Row(rows, "RATE").Fraction, Is.EqualTo(1f), "The fitted minigun is the fastest.");
            Assert.That(Row(rows, "HEAT").Value, Is.EqualTo("NO LIMIT"));
            Assert.That(Row(rows, "RANGE").Unit, Is.EqualTo("M"));
        }

        [Test]
        public void Rocket_LowerIsBetter_ForReloadAndRate()
        {
            var fast = Make<RocketWeaponDefinition>(("fireInterval", 0.2f), ("reloadSeconds", 4f), ("magazineSize", 8f), ("directDamage", 30f),
                ("splashDamage", 20f), ("splashRadius", 6f), ("speed", 200f));
            var slow = Make<RocketWeaponDefinition>(("fireInterval", 1f), ("reloadSeconds", 8f), ("magazineSize", 2f), ("directDamage", 100f),
                ("splashDamage", 60f), ("splashRadius", 12f), ("speed", 120f));
            var rows = new List<HangarStatRow>();

            WeaponStatRows.Build(slow, fast, new WeaponDefinition[] { fast, slow }, rows);

            Assert.That(Row(rows, "DAMAGE").Value, Is.EqualTo("160"), "Direct + splash.");
            Assert.That(Row(rows, "RELOAD").PreviewFraction, Is.EqualTo(0.5f).Within(1e-4f), "4 s best over 8 s.");
            Assert.That(Row(rows, "RATE").PreviewFraction, Is.EqualTo(0.2f).Within(1e-4f));
            Assert.That(Row(rows, "MAGAZINE").Fraction, Is.EqualTo(1f));
            Assert.That(rows.Exists(r => r.Label == "LOCK"), Is.False, "Rockets have no lock.");
            Assert.That(rows.Exists(r => r.Label == "HEAT"), Is.False);
        }

        [Test]
        public void OtherKindFitted_ShowsNoChange()
        {
            var gun = Make<HitscanWeaponDefinition>(("roundsPerMinute", 600f), ("damagePerShot", 10f), ("range", 500f));
            var rocket = Make<RocketWeaponDefinition>(("fireInterval", 0.5f), ("speed", 150f));
            var rows = new List<HangarStatRow>();

            WeaponStatRows.Build(rocket, gun, new WeaponDefinition[] { rocket }, rows);

            Assert.That(rows, Is.Not.Empty);
            foreach (var row in rows) Assert.That(row.Fraction, Is.EqualTo(row.PreviewFraction), row.Label);
        }

        [Test]
        public void NotAWeapon_HasNoRows()
        {
            var rows = new List<HangarStatRow> { new("OLD", 1f, 1f, "1", "", false) };
            WeaponStatRows.Build(null, null, null, rows);
            Assert.That(rows, Is.Empty);
        }
    }
}
