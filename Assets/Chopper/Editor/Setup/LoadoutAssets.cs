using System.Collections.Generic;
using System.Linq;
using Chopper.Simulation.Definitions;
using Chopper.Simulation.Flight;
using Chopper.Simulation.Loadout;
using UnityEditor;
using UnityEngine;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// First version of the loadout data: 4 chassis (with flight tuning), weapon definitions, and modules from
    /// Docs/MultiplayerOverhaulPlan.md section 5. Creates only what is missing; tune the assets by hand afterwards.
    /// </summary>
    internal static class LoadoutAssets
    {
        public const string Folder = "Assets/Chopper/Data/Loadout";
        private const string CatalogPath = Folder + "/LoadoutCatalog.asset";

        /// <summary>The prefab's default loadout: Gunship with gun, two rocket pods, flares and standard parts.</summary>
        private const ushort DefaultChassis = 2;
        private static readonly ushort[] DefaultModules = { 101, 111, 111, 131, 151, 161, 171 };

        private static readonly SlotType[] ScoutSlots =
            { SlotType.Nose, SlotType.Pylon, SlotType.Utility, SlotType.Rotor, SlotType.Engine, SlotType.Armour };
        private static readonly SlotType[] GunshipSlots =
            { SlotType.Nose, SlotType.Pylon, SlotType.Pylon, SlotType.Utility, SlotType.Rotor, SlotType.Engine, SlotType.Armour };
        private static readonly SlotType[] AssaultSlots =
            { SlotType.Nose, SlotType.Pylon, SlotType.Pylon, SlotType.Turret, SlotType.Utility, SlotType.Rotor, SlotType.Engine, SlotType.Armour };
        private static readonly SlotType[] LifterSlots =
            { SlotType.Nose, SlotType.Pylon, SlotType.Utility, SlotType.Utility, SlotType.Winch, SlotType.Rotor, SlotType.Engine, SlotType.Armour };

        public static LoadoutCatalog Ensure()
        {
            var existing = AssetDatabase.LoadAssetAtPath<LoadoutCatalog>(CatalogPath);
            if (existing != null) return existing;
            System.IO.Directory.CreateDirectory(Folder);

            var gunshipFlight = CalibratedGunshipFlight();
            var scout = Chassis(1, "Scout", "Light. Flanks, scouts and fast nuke runs.", 70f, 60f, 0, ScoutSlots,
                Scaled(gunshipFlight, turn: 1.2f, gravity: 1.5f, collective: 1f, boostRecharge: 5f));
            var gunship = Chassis(DefaultChassis, "Gunship", "Medium. All-round fighter.", 100f, 90f, 0, GunshipSlots, gunshipFlight);
            var assault = Chassis(3, "Assault", "Heavy. Tank and base defence. Has a turret.", 150f, 130f, 4, AssaultSlots,
                Scaled(gunshipFlight, turn: 0.75f, gravity: 1.8f, collective: 0.9f, boostRecharge: 7.5f));
            var lifter = Chassis(4, "Lifter", "Support. Winch, repair, best nuke carrier.", 110f, 100f, 6, LifterSlots,
                Scaled(gunshipFlight, turn: 0.85f, gravity: 1.7f, collective: 1.1f, boostRecharge: 6f));

            var minigun = Hitscan("Minigun", rpm: 750f, damage: 8f, range: 600f);
            var autocannon = Hitscan("Autocannon", rpm: 240f, damage: 25f, range: 700f);
            var laser = Hitscan("PulseLaser", rpm: 600f, damage: 10f, range: 500f);
            var rocketPod = Rockets("RocketPod");

            var modules = new List<ModuleDefinition>
            {
                // Nose (hitscan)
                Module(101, "M134 Minigun", "Fast fire, low damage per shot.", SlotType.Nose, Stats(weight: 8f), 0, minigun),
                Module(102, "20mm Autocannon", "Slow, heavy hits.", SlotType.Nose, Stats(weight: 14f), 3, autocannon),
                // Lasers are a late unlock: the world is gritty and kinetic first.
                Module(103, "Pulse Laser", "No bullet drop, overheats.", SlotType.Nose, Stats(weight: 12f), 20, laser),
                // Pylons (projectiles). TODO: weapon definitions for missiles, bombs and mines.
                Module(111, "Rocket Pod", "Unguided rockets with splash.", SlotType.Pylon, Stats(weight: 10f), 0, rocketPod),
                Module(112, "Homing Missiles", "Lock on, then fire.", SlotType.Pylon, Stats(weight: 14f), 5),
                Module(113, "Bombs", "Drop on ground targets and bases.", SlotType.Pylon, Stats(weight: 16f), 10),
                Module(114, "Air Mines", "Hang in the air, explode on contact.", SlotType.Pylon, Stats(weight: 12f), 12),
                // Turret (Assault only)
                Module(121, "Flak Turret", "Auto-aims at the closest enemy.", SlotType.Turret, Stats(weight: 20f), 0, null, assault),
                // Utility
                Module(131, "Flares", "Breaks missile locks.", SlotType.Utility, Stats(weight: 4f), 0),
                Module(132, "Smoke", "Blocks sight lines.", SlotType.Utility, Stats(weight: 4f), 2),
                Module(133, "Repair Drone", "Heals you or a team-mate.", SlotType.Utility, Stats(weight: 8f), 6),
                Module(134, "EMP", "Stops nearby enemy weapons for a short time.", SlotType.Utility, Stats(weight: 10f), 9),
                Module(135, "Shield", "Blocks damage from the front.", SlotType.Utility, Stats(weight: 12f), 14),
                // Winch (Lifter only)
                Module(141, "Heavy Winch", "Carries the nuke faster.", SlotType.Winch, Stats(weight: 10f), 0, null, lifter),
                // Rotor
                Module(151, "Standard Rotor", "Balanced.", SlotType.Rotor, Stats(weight: 10f), 0),
                Module(152, "Agile Rotor", "Turns faster, less lift.", SlotType.Rotor, Stats(weight: 10f, lift: 0.95f, turn: 1.15f), 4),
                Module(153, "Heavy-Lift Rotor", "More lift, turns slower.", SlotType.Rotor, Stats(weight: 12f, lift: 1.12f, turn: 0.92f), 7),
                // Engine
                Module(161, "Standard Engine", "Balanced.", SlotType.Engine, Stats(weight: 12f), 0),
                Module(162, "Boost Engine", "Boost tank refills faster.", SlotType.Engine, Stats(weight: 14f, boost: 0.75f), 5),
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
            Debug.Log($"Created {CatalogPath}: 4 chassis, {modules.Count} modules, 4 weapons.");
            return catalog;
        }

        /// <summary>Chassis and modules of the prefab's default loadout, in slot order.</summary>
        public static (ChassisDefinition Chassis, ModuleDefinition[] Modules) DefaultLoadout(LoadoutCatalog catalog)
        {
            var chassis = catalog.Chassis.FirstOrDefault(c => c != null && c.Id == DefaultChassis);
            var modules = DefaultModules
                .Select(id => catalog.Modules.FirstOrDefault(m => m != null && m.Id == id))
                .ToArray();
            return (chassis, modules);
        }

        /// <summary>
        /// The tuned Little Bird feel is the Gunship with its default loadout. Weight slows turning and lift
        /// (<see cref="LoadoutRules"/>), so the base values are raised by that much: the default loadout then flies
        /// exactly as tuned.
        /// </summary>
        private static FlightTuning CalibratedGunshipFlight()
        {
            const float defaultLoadoutWeight = 8f + 10f + 10f + 4f + 10f + 12f + 6f;
            const float gunshipBudget = 90f;
            var fraction = defaultLoadoutWeight / gunshipBudget;

            var tuned = new FlightTuning();
            var turn = 1f / (1f - LoadoutRules.FullWeightTurnPenalty * fraction);
            var lift = 1f / (1f - LoadoutRules.FullWeightLiftPenalty * fraction);
            tuned.MaxPitchRate *= turn;
            tuned.MaxYawRate *= turn;
            tuned.MaxRollRate *= turn;
            tuned.CollectiveUp *= lift;
            return tuned;
        }

        private static FlightTuning Scaled(FlightTuning source, float turn, float gravity, float collective, float boostRecharge)
        {
            var flight = source.Clone();
            flight.MaxPitchRate *= turn;
            flight.MaxYawRate *= turn;
            flight.MaxRollRate *= turn;
            flight.GravityScale = gravity;
            flight.CollectiveUp *= collective;
            flight.BoostRechargeTime = boostRecharge;
            return flight;
        }

        private static ChassisDefinition Chassis(ushort id, string name, string text, float health, float budget, int level,
            SlotType[] slots, FlightTuning flight)
        {
            var asset = ScriptableObject.CreateInstance<ChassisDefinition>();
            asset.Initialise(id, name, text, health, budget, level, slots, flight);
            AssetDatabase.CreateAsset(asset, $"{Folder}/Chassis_{name}.asset");
            return asset;
        }

        private static HitscanWeaponDefinition Hitscan(string name, float rpm, float damage, float range)
        {
            var asset = ScriptableObject.CreateInstance<HitscanWeaponDefinition>();
            asset.Initialise(rpm, damage, range);
            asset.SetMuzzleOffset(new Vector3(0f, -0.6f, 2.5f));
            AssetDatabase.CreateAsset(asset, $"{Folder}/Weapon_{name}.asset");
            return asset;
        }

        private static RocketWeaponDefinition Rockets(string name)
        {
            var asset = ScriptableObject.CreateInstance<RocketWeaponDefinition>();
            asset.SetMuzzleOffset(new Vector3(1.4f, -0.8f, 1.5f));
            AssetDatabase.CreateAsset(asset, $"{Folder}/Weapon_{name}.asset");
            return asset;
        }

        private static ModuleDefinition Module(ushort id, string name, string text, SlotType slot, ModuleStats stats, int level,
            WeaponDefinition weapon = null, params ChassisDefinition[] chassis)
        {
            var asset = ScriptableObject.CreateInstance<ModuleDefinition>();
            asset.Initialise(id, name, text, slot, stats, level, chassis);
            asset.SetWeapon(weapon);
            AssetDatabase.CreateAsset(asset, $"{Folder}/{slot}_{name.Replace(" ", "")}.asset");
            return asset;
        }

        private static ModuleStats Stats(float weight, float health = 0f, float lift = 1f, float turn = 1f, float boost = 1f) => new()
        {
            Weight = weight,
            HealthBonus = health,
            LiftMultiplier = lift,
            TurnRateMultiplier = turn,
            BoostRechargeMultiplier = boost,
        };
    }
}
