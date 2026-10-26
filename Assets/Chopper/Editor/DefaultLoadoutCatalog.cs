using System.Collections.Generic;
using Chopper.Simulation.Loadout;
using UnityEditor;
using UnityEngine;

namespace Chopper.Editor
{
    /// <summary>
    /// Creates the first version of the loadout data (4 chassis, starter modules) from Docs/MultiplayerOverhaulPlan.md
    /// section 5. Run once, then tune the assets by hand. Does nothing if the catalog already exists.
    /// </summary>
    internal static class DefaultLoadoutCatalog
    {
        private const string Folder = "Assets/Chopper/Data/Loadout";
        private const string CatalogPath = Folder + "/LoadoutCatalog.asset";

        private static readonly SlotType[] ScoutSlots =
            { SlotType.Nose, SlotType.Pylon, SlotType.Utility, SlotType.Rotor, SlotType.Engine, SlotType.Armour };
        private static readonly SlotType[] GunshipSlots =
            { SlotType.Nose, SlotType.Pylon, SlotType.Pylon, SlotType.Utility, SlotType.Rotor, SlotType.Engine, SlotType.Armour };
        private static readonly SlotType[] AssaultSlots =
            { SlotType.Nose, SlotType.Pylon, SlotType.Pylon, SlotType.Turret, SlotType.Utility, SlotType.Rotor, SlotType.Engine, SlotType.Armour };
        private static readonly SlotType[] LifterSlots =
            { SlotType.Nose, SlotType.Pylon, SlotType.Utility, SlotType.Utility, SlotType.Winch, SlotType.Rotor, SlotType.Engine, SlotType.Armour };

        [MenuItem("Chopper/Loadout/Create Default Catalog")]
        private static void Create()
        {
            if (AssetDatabase.LoadAssetAtPath<LoadoutCatalog>(CatalogPath) != null)
            {
                Debug.Log($"{CatalogPath} already exists. Delete it first to make a new one.");
                return;
            }
            System.IO.Directory.CreateDirectory(Folder);

            var scout = Chassis(1, "Scout", "Light. Flanks, scouts and fast nuke runs.", 70f, 60f, 0, ScoutSlots);
            var gunship = Chassis(2, "Gunship", "Medium. All-round fighter.", 100f, 90f, 0, GunshipSlots);
            var assault = Chassis(3, "Assault", "Heavy. Tank and base defence. Has a turret.", 150f, 130f, 4, AssaultSlots);
            var lifter = Chassis(4, "Lifter", "Support. Winch, repair, best nuke carrier.", 110f, 100f, 6, LifterSlots);

            var modules = new List<ModuleDefinition>
            {
                // Nose (hitscan)
                Module(101, "M134 Minigun", "Fast fire, low damage per shot.", SlotType.Nose, Stats(weight: 8f), 0),
                Module(102, "20mm Autocannon", "Slow, heavy hits.", SlotType.Nose, Stats(weight: 14f), 3),
                Module(103, "Pulse Laser", "No bullet drop, overheats.", SlotType.Nose, Stats(weight: 12f), 8),
                // Pylons (projectiles)
                Module(111, "Rocket Pod", "Unguided rockets with splash.", SlotType.Pylon, Stats(weight: 10f), 0),
                Module(112, "Homing Missiles", "Lock on, then fire.", SlotType.Pylon, Stats(weight: 14f), 5),
                Module(113, "Bombs", "Drop on ground targets and bases.", SlotType.Pylon, Stats(weight: 16f), 10),
                Module(114, "Air Mines", "Hang in the air, explode on contact.", SlotType.Pylon, Stats(weight: 12f), 12),
                // Turret (Assault only)
                Module(121, "Flak Turret", "Auto-aims at the closest enemy.", SlotType.Turret, Stats(weight: 20f), 0, assault),
                // Utility
                Module(131, "Flares", "Breaks missile locks.", SlotType.Utility, Stats(weight: 4f), 0),
                Module(132, "Smoke", "Blocks sight lines.", SlotType.Utility, Stats(weight: 4f), 2),
                Module(133, "Repair Drone", "Heals you or a team-mate.", SlotType.Utility, Stats(weight: 8f), 6),
                Module(134, "EMP", "Stops nearby enemy weapons for a short time.", SlotType.Utility, Stats(weight: 10f), 9),
                Module(135, "Shield", "Blocks damage from the front.", SlotType.Utility, Stats(weight: 12f), 14),
                // Winch (Lifter only)
                Module(141, "Heavy Winch", "Carries the nuke faster.", SlotType.Winch, Stats(weight: 10f), 0, lifter),
                // Rotor
                Module(151, "Standard Rotor", "Balanced.", SlotType.Rotor, Stats(weight: 10f), 0),
                Module(152, "Agile Rotor", "Turns faster, less lift.", SlotType.Rotor, Stats(weight: 10f, lift: 0.95f, turn: 1.15f), 4),
                Module(153, "Heavy-Lift Rotor", "More lift, turns slower.", SlotType.Rotor, Stats(weight: 12f, lift: 1.12f, turn: 0.92f), 7),
                // Engine
                Module(161, "Standard Engine", "Balanced.", SlotType.Engine, Stats(weight: 12f), 0),
                Module(162, "Boost Engine", "Shorter dash cooldown.", SlotType.Engine, Stats(weight: 14f, dash: 0.75f), 5),
                Module(163, "Turbine", "More lift.", SlotType.Engine, Stats(weight: 16f, lift: 1.08f), 11),
                // Armour
                Module(171, "Light Armour", "+10 HP.", SlotType.Armour, Stats(weight: 6f, health: 10f), 0),
                Module(172, "Medium Armour", "+30 HP.", SlotType.Armour, Stats(weight: 14f, health: 30f), 3),
                Module(173, "Heavy Armour", "+60 HP, less lift.", SlotType.Armour, Stats(weight: 24f, health: 60f, lift: 0.95f), 8),
            };

            var catalog = ScriptableObject.CreateInstance<LoadoutCatalog>();
            catalog.Initialise(new[] { scout, gunship, assault, lifter }, modules.ToArray());
            AssetDatabase.CreateAsset(catalog, CatalogPath);
            AssetDatabase.SaveAssets();
            Selection.activeObject = catalog;
            Debug.Log($"Created {CatalogPath} with 4 chassis and {modules.Count} modules.");
        }

        private static ChassisDefinition Chassis(ushort id, string name, string text, float health, float budget, int level, SlotType[] slots)
        {
            var asset = ScriptableObject.CreateInstance<ChassisDefinition>();
            asset.Initialise(id, name, text, health, budget, level, slots);
            AssetDatabase.CreateAsset(asset, $"{Folder}/Chassis_{name}.asset");
            return asset;
        }

        private static ModuleDefinition Module(ushort id, string name, string text, SlotType slot, ModuleStats stats, int level,
            params ChassisDefinition[] chassis)
        {
            var asset = ScriptableObject.CreateInstance<ModuleDefinition>();
            asset.Initialise(id, name, text, slot, stats, level, chassis);
            AssetDatabase.CreateAsset(asset, $"{Folder}/{slot}_{name.Replace(" ", "")}.asset");
            return asset;
        }

        private static ModuleStats Stats(float weight, float health = 0f, float lift = 1f, float turn = 1f, float dash = 1f) => new()
        {
            Weight = weight,
            HealthBonus = health,
            LiftMultiplier = lift,
            TurnRateMultiplier = turn,
            DashCooldownMultiplier = dash,
        };
    }
}
