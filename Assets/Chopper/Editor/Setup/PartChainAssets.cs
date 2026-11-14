using System;
using System.Linq;
using Chopper.Simulation.Loadout;
using UnityEditor;
using UnityEngine;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// Chassis mastery part chains (design doc "Chassis Mastery Unlocks", proposals A and B):
    /// A = Mk II and Mk III of the Standard rotor, Standard engine and Light armour, for every class;
    /// B = one class-only line per class (Scout Agile rotor, Gunship Boost engine, Assault Medium armour,
    /// Lifter Heavy-Lift rotor). Each tier copies its parent and changes a few stats: a trade, not a plain upgrade.
    /// Creates only what is missing; an existing tier asset keeps its tuned values.
    /// </summary>
    internal static class PartChainAssets
    {
        private const string Folder = "Assets/Chopper/Data/Loadout";

        /// <summary>Mastery for shared chains (A) and class lines (B).</summary>
        private const int SharedMk2 = 3, SharedMk3 = 8, ClassMk2 = 5, ClassMk3 = 12;

        private const ushort StandardRotor = 151, AgileRotor = 152, HeavyLiftRotor = 153;
        private const ushort StandardEngine = 161, BoostEngine = 162;
        private const ushort LightArmour = 171, MediumArmour = 172;

        /// <summary>One tier: its id, the module before it, mastery, class (None = all), and how it changes the parent.</summary>
        private readonly struct Tier
        {
            public readonly ushort Id;
            public readonly ushort Parent;
            public readonly string Mark;
            public readonly int Mastery;
            public readonly ChassisClass Only;
            public readonly string Trade;
            public readonly Func<ModuleStats, ModuleStats> Change;

            public Tier(ushort id, ushort parent, string mark, int mastery, ChassisClass only, string trade, Func<ModuleStats, ModuleStats> change)
            {
                Id = id;
                Parent = parent;
                Mark = mark;
                Mastery = mastery;
                Only = only;
                Trade = trade;
                Change = change;
            }
        }

        // Order matters: a Mk III's parent (the Mk II) is made first. First-guess numbers; tune in the inspector.
        private static readonly Tier[] Tiers =
        {
            // ---- A: shared chains ----
            new(154, StandardRotor, "Mk II", SharedMk2, ChassisClass.None, "Sharper turns, a little heavier.",
                s => { s.TurnRateMultiplier *= 1.06f; s.Weight += 2f; return s; }),
            new(155, 154, "Mk III", SharedMk3, ChassisClass.None, "Much sharper turns; heavier, less lift.",
                s => { s.TurnRateMultiplier *= 1.06f; s.LiftMultiplier *= 0.95f; s.Weight += 2f; return s; }),
            new(164, StandardEngine, "Mk II", SharedMk2, ChassisClass.None, "Faster top speed, a little heavier.",
                s => { s.SpeedMultiplier *= 1.08f; s.Weight += 2f; return s; }),
            new(165, 164, "Mk III", SharedMk3, ChassisClass.None, "Much faster; heavier, the boost refills slower.",
                s => { s.SpeedMultiplier *= 1.07f; s.BoostRechargeMultiplier *= 1.15f; s.Weight += 2f; return s; }),
            new(174, LightArmour, "Mk II", SharedMk2, ChassisClass.None, "More health, heavier.",
                s => { s.HealthBonus += 10f; s.Weight += 3f; return s; }),
            new(175, 174, "Mk III", SharedMk3, ChassisClass.None, "Much more health; heavier, slower turns.",
                s => { s.HealthBonus += 10f; s.TurnRateMultiplier *= 0.95f; s.Weight += 3f; return s; }),

            // ---- B: class-only lines ----
            new(156, AgileRotor, "Mk II", ClassMk2, ChassisClass.Scout, "Scout only. Sharper turns, less health.",
                s => { s.TurnRateMultiplier *= 1.06f; s.HealthBonus -= 5f; return s; }),
            new(157, 156, "Mk III", ClassMk3, ChassisClass.Scout, "Scout only. Sharpest turns, even less health.",
                s => { s.TurnRateMultiplier *= 1.06f; s.HealthBonus -= 5f; return s; }),
            new(166, BoostEngine, "Mk II", ClassMk2, ChassisClass.Gunship, "Gunship only. Boost refills faster and pushes harder; heavier.",
                s => { s.BoostRechargeMultiplier *= 0.9f; s.BoostPowerMultiplier *= 1.05f; s.Weight += 2f; return s; }),
            new(167, 166, "Mk III", ClassMk3, ChassisClass.Gunship, "Gunship only. The strongest boost; heavier still.",
                s => { s.BoostRechargeMultiplier *= 0.9f; s.BoostPowerMultiplier *= 1.05f; s.Weight += 2f; return s; }),
            new(176, MediumArmour, "Mk II", ClassMk2, ChassisClass.Assault, "Assault only. More health, slower turns, heavier.",
                s => { s.HealthBonus += 10f; s.TurnRateMultiplier *= 0.97f; s.Weight += 3f; return s; }),
            new(177, 176, "Mk III", ClassMk3, ChassisClass.Assault, "Assault only. The most health; slower and heavier.",
                s => { s.HealthBonus += 10f; s.TurnRateMultiplier *= 0.97f; s.Weight += 3f; return s; }),
            new(158, HeavyLiftRotor, "Mk II", ClassMk2, ChassisClass.Lifter, "Lifter only. More lift, lower top speed.",
                s => { s.LiftMultiplier *= 1.04f; s.SpeedMultiplier *= 0.97f; s.Weight += 1f; return s; }),
            new(159, 158, "Mk III", ClassMk3, ChassisClass.Lifter, "Lifter only. The most lift; slower again.",
                s => { s.LiftMultiplier *= 1.04f; s.SpeedMultiplier *= 0.97f; s.Weight += 1f; return s; }),
        };

        public static void Ensure(LoadoutCatalog catalog)
        {
            if (catalog == null) return;
            foreach (var tier in Tiers) EnsureTier(catalog, tier);
            AssetDatabase.SaveAssets();
        }

        private static void EnsureTier(LoadoutCatalog catalog, Tier tier)
        {
            if (catalog.Modules.Any(m => m != null && m.Id == tier.Id)) return;
            var parent = catalog.Modules.FirstOrDefault(m => m != null && m.Id == tier.Parent);
            if (parent == null)
            {
                Debug.LogWarning($"Setup: part chain tier {tier.Id} skipped: its parent module {tier.Parent} is not in the catalog.");
                return;
            }

            // The chain's base name: "Agile Rotor" for both the Mk II and the Mk III.
            var baseName = parent.DisplayName.Replace(" Mk II", "");
            var name = $"{baseName} {tier.Mark}";
            var only = tier.Only == ChassisClass.None
                ? null
                : catalog.Chassis.Where(c => c != null && c.Class == tier.Only).ToArray();

            var module = ScriptableObject.CreateInstance<ModuleDefinition>();
            module.Initialise(tier.Id, name, tier.Trade, parent.Slot, tier.Change(parent.Stats), parent.UnlockLevel, only);
            module.SetChain(parent, tier.Mastery);
            // Same look as the part it upgrades, until tiers get their own art.
            module.SetVisual(parent.Visual);
            module.SetVisualSpin(parent.VisualSpin);
            AssetDatabase.CreateAsset(module, $"{Folder}/{parent.Slot}_{name.Replace(" ", "").Replace("-", "")}.asset");

            catalog.Initialise(catalog.Chassis.ToArray(), catalog.Modules.Append(module).ToArray());
            EditorUtility.SetDirty(catalog);
        }
    }
}
