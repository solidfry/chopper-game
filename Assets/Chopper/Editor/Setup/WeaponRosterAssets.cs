using System.Linq;
using Chopper.Simulation.Combat;
using Chopper.Simulation.Definitions;
using Chopper.Simulation.Loadout;
using UnityEditor;
using UnityEngine;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// The larger weapon roster (plan "Rockets and a rich weapon roster"): more nose guns, more rocket types, weapons for
    /// the Bombs and Air Mines modules, and the first part chains (Mk II tiers on chassis mastery). All on the existing
    /// gun and rocket behaviours: only new data. Creates or links only what is missing; never overwrites tuned values.
    /// </summary>
    internal static class WeaponRosterAssets
    {
        // Nose guns.
        public const ushort HeavyMgId = 104;
        public const ushort RotaryId = 105;
        public const ushort ChainGunId = 106;
        public const ushort RailgunId = 107;
        public const ushort MinigunMk2Id = 108;
        // Pylons (121 is the Flak Turret).
        public const ushort LightRocketsId = 118;
        public const ushort HeavyRocketsId = 119;
        public const ushort SalvoPodId = 120;
        public const ushort FlakRocketsId = 122;
        public const ushort RocketPodMk2Id = 124;
        // Utility: optics (zoom).
        public const ushort TargetingOpticsId = 136;
        public const ushort LongRangeScopeId = 137;

        private const ushort MinigunId = 101;
        private const ushort RocketPodId = 111;
        private const ushort BombsId = 113;
        private const ushort AirMinesId = 114;
        /// <summary>Chassis class mastery level for a Mk II tier.</summary>
        private const int Mk2Mastery = 4;

        private static readonly Vector3 NoseMuzzle = new(0f, -0.6f, 2.5f);
        private static readonly Vector3 PylonMuzzle = new(1.4f, -0.8f, 1.5f);

        public static void Ensure(LoadoutCatalog catalog)
        {
            if (catalog == null) return;
            EnsureGuns(catalog);
            EnsureRockets(catalog);
            EnsureOptics(catalog);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Highest zoom per gun: short-range guns get little from a scope, the railgun gets all of it.</summary>
        private static readonly (string Asset, float MaxZoom)[] GunZoom =
        {
            ("Weapon_Minigun", 2f), ("Weapon_MinigunMk2", 2f), ("Weapon_RotaryCannon", 2f), ("Weapon_HeavyMG", 3f),
            ("Weapon_Autocannon", 4f), ("Weapon_PulseLaser", 4f), ("Weapon_ChainGun", 6f), ("Weapon_Railgun", 10f),
        };

        /// <summary>
        /// Zoom sights (utility slot) and each gun's zoom limit. A gun asset that still has the default limit (2) gets
        /// its value from the table; a value set by hand is kept.
        /// </summary>
        private static void EnsureOptics(LoadoutCatalog catalog)
        {
            foreach (var (asset, maxZoom) in GunZoom)
            {
                var gun = AssetDatabase.LoadAssetAtPath<HitscanWeaponDefinition>($"{LoadoutAssets.Folder}/{asset}.asset");
                if (gun == null || !Mathf.Approximately(gun.MaxZoom, DefaultMaxZoom) || Mathf.Approximately(maxZoom, DefaultMaxZoom)) continue;
                gun.SetMaxZoom(maxZoom);
                EditorUtility.SetDirty(gun);
            }

            var targeting = GuidedWeaponAssets.Definition<OpticsDefinition>("Ability_TargetingOptics", d => d.Initialise(2f, 4f));
            var scope = GuidedWeaponAssets.Definition<OpticsDefinition>("Ability_LongRangeScope", d => d.Initialise(3f, 6f, 10f));
            Optics(catalog, TargetingOpticsId, "Targeting Optics", "Zoom 2x and 4x. The gun limits how far.", "Utility_TargetingOptics", 5f, 3,
                targeting);
            Optics(catalog, LongRangeScopeId, "Long-Range Scope", "Zoom 3x, 6x and 10x. Heavy. Best on a railgun.", "Utility_LongRangeScope",
                9f, 14, scope);
        }

        /// <summary>The zoom limit of a new gun asset (the field's default).</summary>
        private const float DefaultMaxZoom = 2f;

        private static void Optics(LoadoutCatalog catalog, ushort id, string displayName, string description, string assetName,
            float weight, int unlockLevel, OpticsDefinition optics)
        {
            var module = GuidedWeaponAssets.EnsureModule(catalog, id, displayName, description, assetName, weight, unlockLevel, null,
                SlotType.Utility);
            if (module == null || module.Ability != null || module.Weapon != null) return;
            module.SetAbility(optics);
            EditorUtility.SetDirty(module);
        }

        /// <summary>
        /// Guns trade rate, damage, range and heat. Close-range damage per second stays near 100 (as the first three
        /// guns); the trade is in how it is dealt.
        /// </summary>
        private static void EnsureGuns(LoadoutCatalog catalog)
        {
            // Middle of the road: more reach and a longer burst than the minigun, slower fire.
            var heavyMg = Gun("Weapon_HeavyMG", rpm: 450f, damage: 14f, range: 650f, heatSeconds: 7f, coolSeconds: 4f);
            // Close-in shredder: the most damage per second near, half of it at range, overheats fast.
            var rotary = Gun("Weapon_RotaryCannon", rpm: 1500f, damage: 4.5f, range: 450f, heatSeconds: 3.5f, coolSeconds: 4f,
                falloffStart: 200f, falloffFloor: 0.5f);
            // Slow, heavy shells with reach: every shot must count.
            var chainGun = Gun("Weapon_ChainGun", rpm: 150f, damage: 42f, range: 750f, heatSeconds: 5f, coolSeconds: 5f);
            // One huge hit every 2 s, the longest range, three shots to overheat. Late unlock.
            var railgun = Gun("Weapon_Railgun", rpm: 30f, damage: 90f, range: 1200f, heatSeconds: 5f, coolSeconds: 6f);
            // Part chain tier: the minigun with a longer burst.
            var minigunMk2 = Gun("Weapon_MinigunMk2", rpm: 750f, damage: 8f, range: 600f, heatSeconds: 7.5f, coolSeconds: 3.5f);

            Nose(catalog, HeavyMgId, "GAU-19 Heavy MG", "Steady fire with reach. Slow to overheat.", "Nose_HeavyMG", 11f, 2, heavyMg);
            Nose(catalog, RotaryId, "Rotary Cannon", "Shreds up close. Weak at range, overheats fast.", "Nose_RotaryCannon", 13f, 7, rotary);
            Nose(catalog, ChainGunId, "30mm Chain Gun", "Slow, heavy shells. Long range.", "Nose_ChainGun", 18f, 12, chainGun);
            Nose(catalog, RailgunId, "Railgun", "One huge hit every two seconds. Longest range.", "Nose_Railgun", 20f, 30, railgun);
            var mk2 = Nose(catalog, MinigunMk2Id, "M134 Minigun Mk II", "Longer bursts, cools faster.", "Nose_MinigunMk2", 8f, 0, minigunMk2);
            Chain(catalog, mk2, MinigunId);
        }

        private static void EnsureRockets(LoadoutCatalog catalog)
        {
            // Fast and flat: easy to hit with, weak, many rounds.
            var light = RocketDef("Weapon_LightRockets", interval: 0.25f, speed: 220f, life: 3.5f, direct: 16f, splash: 10f, radius: 4f,
                magazine: 24, reload: 7f);
            // Slow, with a small drop: a big blast for ground targets and slow choppers.
            var heavy = RocketDef("Weapon_HeavyRockets", interval: 1.6f, speed: 95f, life: 6f, direct: 70f, splash: 55f, radius: 14f,
                magazine: 4, reload: 10f, r => r.SetPath(gravityAcceleration: 4f, dragRate: 0f));
            // Six small rockets per pull, fanned 3 degrees: area cover, long wait between salvos.
            var salvo = RocketDef("Weapon_SalvoPod", interval: 3f, speed: 150f, life: 4f, direct: 14f, splash: 10f, radius: 5f,
                magazine: 3, reload: 12f, r => r.SetSalvo(count: 6, interval: 0.07f, spreadDegrees: 3f));
            // Anti-air: bursts near a chopper, so a near miss still hurts. Low direct damage.
            var flak = RocketDef("Weapon_FlakRockets", interval: 0.8f, speed: 170f, life: 3.5f, direct: 20f, splash: 28f, radius: 9f,
                magazine: 8, reload: 9f, r => r.SetFuse(radius: 6f, TargetClass.Air));
            // A bomb is a rocket with gravity and almost no push: it keeps the chopper's speed and falls.
            var bombs = RocketDef("Weapon_Bombs", interval: 1.2f, speed: 4f, life: 12f, direct: 90f, splash: 80f, radius: 18f,
                magazine: 4, reload: 14f, r => r.SetPath(gravityAcceleration: 25f, dragRate: 0f));
            // A mine is a rocket that stops (drag) and waits with a proximity fuse. It also keeps the chopper's speed at first.
            var mines = RocketDef("Weapon_AirMines", interval: 1.5f, speed: 20f, life: 25f, direct: 40f, splash: 60f, radius: 14f,
                magazine: 5, reload: 16f, r =>
                {
                    r.SetPath(gravityAcceleration: 0f, dragRate: 2f);
                    r.SetFuse(radius: 10f, TargetClass.Air);
                });
            // Part chain tier: the rocket pod with more rockets and a faster rate.
            var podMk2 = RocketDef("Weapon_RocketPodMk2", interval: 0.5f, speed: 140f, life: 4f, direct: 30f, splash: 25f, radius: 8f,
                magazine: 19, reload: 6f);

            Pylon(catalog, LightRocketsId, "Light Rockets", "Fast, flat and weak. Many rounds.", "Pylon_LightRockets", 8f, 2, light);
            Pylon(catalog, HeavyRocketsId, "Heavy Rockets", "Slow, they drop a little. Big blast.", "Pylon_HeavyRockets", 16f, 6, heavy);
            Pylon(catalog, SalvoPodId, "Salvo Pod", "Six rockets per pull. Long wait between salvos.", "Pylon_SalvoPod", 13f, 9, salvo);
            Pylon(catalog, FlakRocketsId, "Flak Rockets", "Burst near aircraft: a near miss still hurts.", "Pylon_FlakRockets", 12f, 11, flak);
            var mk2 = Pylon(catalog, RocketPodMk2Id, "Rocket Pod Mk II", "More rockets, faster fire.", "Pylon_RocketPodMk2", 10f, 0, podMk2);
            Chain(catalog, mk2, RocketPodId);

            // These two modules were in the first catalog with no weapon.
            GuidedWeaponAssets.Link(catalog, BombsId, m => m.Weapon == null, m => m.SetWeapon(bombs));
            GuidedWeaponAssets.Link(catalog, AirMinesId, m => m.Weapon == null, m => m.SetWeapon(mines));
        }

        private static HitscanWeaponDefinition Gun(string assetName, float rpm, float damage, float range, float heatSeconds,
            float coolSeconds, float falloffStart = 0f, float falloffFloor = 0.5f) =>
            GuidedWeaponAssets.Definition<HitscanWeaponDefinition>(assetName, d =>
            {
                d.Initialise(rpm, damage, range);
                d.SetHeat(heatSeconds, coolSeconds);
                d.SetFalloff(falloffStart, falloffFloor);
                d.SetMuzzleOffset(NoseMuzzle);
            });

        private static RocketWeaponDefinition RocketDef(string assetName, float interval, float speed, float life, float direct, float splash,
            float radius, int magazine, float reload, System.Action<RocketWeaponDefinition> extra = null) =>
            GuidedWeaponAssets.Definition<RocketWeaponDefinition>(assetName, d =>
            {
                d.Initialise(interval, speed, life, direct, splash, radius, magazine, reload);
                extra?.Invoke(d);
                d.SetMuzzleOffset(PylonMuzzle);
            });

        private static ModuleDefinition Nose(LoadoutCatalog catalog, ushort id, string displayName, string description, string assetName,
            float weight, int unlockLevel, WeaponDefinition weapon) =>
            GuidedWeaponAssets.EnsureModule(catalog, id, displayName, description, assetName, weight, unlockLevel, weapon, SlotType.Nose);

        private static ModuleDefinition Pylon(LoadoutCatalog catalog, ushort id, string displayName, string description, string assetName,
            float weight, int unlockLevel, WeaponDefinition weapon) =>
            GuidedWeaponAssets.EnsureModule(catalog, id, displayName, description, assetName, weight, unlockLevel, weapon);

        /// <summary>Makes <paramref name="tier"/> the next tier after module <paramref name="parentId"/> (only when it has no parent yet).</summary>
        private static void Chain(LoadoutCatalog catalog, ModuleDefinition tier, ushort parentId)
        {
            if (tier == null || tier.Parent != null) return;
            var parent = catalog.Modules.FirstOrDefault(m => m != null && m.Id == parentId);
            if (parent == null) return;
            tier.SetChain(parent, Mk2Mastery);
            EditorUtility.SetDirty(tier);
        }
    }
}
