using System.Linq;
using Chopper.Simulation.Combat;
using Chopper.Simulation.Definitions;
using Chopper.Simulation.Loadout;
using UnityEditor;
using UnityEngine;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// Guided weapons and flares: the missile ghost prefab, heat-seeker definitions (air-to-air, air-to-ground), the
    /// cluster missile, the flares ability, and their links to modules (Homing Missiles, new Ground Seeker and Cluster
    /// Missiles modules, Flares). Creates or links only
    /// what is missing; never overwrites tuned values.
    /// </summary>
    internal static class GuidedWeaponAssets
    {
        public const string MissilePrefabPath = "Assets/Chopper/Prefabs/GuidedMissileGhost.prefab";
        private const string Folder = LoadoutAssets.Folder;

        public const ushort HomingMissilesId = 112;
        public const ushort GroundSeekerId = 115;
        public const ushort ClusterMissilesId = 116;
        private const ushort FlaresId = 131;

        public static void Ensure(LoadoutCatalog catalog)
        {
            if (catalog == null) return;
            var prefab = EnsureMissilePrefab();

            var airToAir = Definition<GuidedMissileDefinition>("Weapon_HeatSeekerAA", d =>
            {
                // Agile, needs a lock: a dogfight missile.
                d.Initialise(prefab, TargetClass.Air, needsLock: true, cone: 15f, range: 600f, lockSeconds: 1.2f, missileSpeed: 160f,
                    turnDegreesPerSecond: 120f, gain: 3f, fuse: 6f, direct: 45f, splash: 20f, radius: 6f, interval: 2.5f, life: 6f);
                d.SetMuzzleOffset(new Vector3(1.4f, -0.8f, 1.5f));
            });
            var airToGround = Definition<GuidedMissileDefinition>("Weapon_HeatSeekerAG", d =>
            {
                // Slower and heavier; can dumb-fire at the ground.
                d.Initialise(prefab, TargetClass.Ground, needsLock: false, cone: 10f, range: 800f, lockSeconds: 1.5f, missileSpeed: 110f,
                    turnDegreesPerSecond: 60f, gain: 2f, fuse: 3f, direct: 60f, splash: 35f, radius: 9f, interval: 3f, life: 8f);
                d.SetMuzzleOffset(new Vector3(1.4f, -0.8f, 1.5f));
            });
            var cluster = Definition<GuidedMissileDefinition>("Weapon_ClusterMissile", d =>
            {
                // Area denial: flies at the ground target (or straight when dumb-fired), splits into 8 bomblets.
                d.Initialise(prefab, TargetClass.Ground, needsLock: false, cone: 12f, range: 900f, lockSeconds: 1.5f, missileSpeed: 120f,
                    turnDegreesPerSecond: 45f, gain: 2f, fuse: 3f, direct: 30f, splash: 15f, radius: 6f, interval: 5f, life: 6f);
                d.SetCluster(count: 8, distance: 60f, time: 1.8f, subSpeed: 90f, spread: 14f, subLifetime: 2.5f, direct: 20f, splash: 18f,
                    radius: 6f);
                d.SetMuzzleOffset(new Vector3(1.4f, -0.8f, 1.5f));
            });
            var flares = Definition<CountermeasureDefinition>("Ability_Flares", _ => { });

            Link(catalog, HomingMissilesId, m => m.Weapon == null, m => m.SetWeapon(airToAir));
            Link(catalog, FlaresId, m => m.Ability == null, m => m.SetAbility(flares));
            EnsureModule(catalog, GroundSeekerId, "Ground Seeker", "Heat-seeking air-to-ground missile. Dumb-fires without a lock.",
                "Pylon_GroundSeeker", weight: 16f, unlockLevel: 6, airToGround);
            EnsureModule(catalog, ClusterMissilesId, "Cluster Missiles", "Splits into bomblets over the target. Area denial.",
                "Pylon_ClusterMissiles", weight: 18f, unlockLevel: 8, cluster);
            AssetDatabase.SaveAssets();
        }

        /// <summary>A ghost with no mesh: presentation draws the missile. Owner-predicted, so the shooter predicts it.</summary>
        private static GameObject EnsureMissilePrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(MissilePrefabPath);
            if (existing != null) return existing;

            var go = new GameObject("GuidedMissileGhost");
            try
            {
                go.AddComponent<GuidedMissileAuthoring>();
                var ghostType = TypeCache.GetTypesDerivedFrom<MonoBehaviour>().FirstOrDefault(t => t.Name == "GhostAuthoringComponent");
                if (ghostType == null)
                {
                    Debug.LogError("Setup: GhostAuthoringComponent type not found. Is Netcode for Entities installed?");
                    return null;
                }

                var ghost = new SerializedObject(go.AddComponent(ghostType));
                SetInt(ghost, "DefaultGhostMode", 2);    // OwnerPredicted
                SetInt(ghost, "SupportedGhostModes", 3); // All
                SetInt(ghost, "OptimizationMode", 0);    // Dynamic
                SetInt(ghost, "Importance", 10);         // Missiles matter: send them often.
                SetBool(ghost, "HasOwner", true);
                SetBool(ghost, "SupportAutoCommandTarget", false);
                ghost.ApplyModifiedPropertiesWithoutUndo();

                return PrefabUtility.SaveAsPrefabAsset(go, MissilePrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static T Definition<T>(string assetName, System.Action<T> initialise) where T : ScriptableObject
        {
            var path = $"{Folder}/{assetName}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;

            var asset = ScriptableObject.CreateInstance<T>();
            initialise(asset);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void Link(LoadoutCatalog catalog, ushort moduleId, System.Func<ModuleDefinition, bool> isEmpty,
            System.Action<ModuleDefinition> link)
        {
            var module = catalog.Modules.FirstOrDefault(m => m != null && m.Id == moduleId);
            if (module == null || !isEmpty(module)) return;
            link(module);
            EditorUtility.SetDirty(module);
        }

        /// <summary>A pylon module for a weapon that has no module in the original catalog.</summary>
        private static void EnsureModule(LoadoutCatalog catalog, ushort id, string displayName, string description, string assetName,
            float weight, int unlockLevel, GuidedMissileDefinition weapon)
        {
            if (catalog.Modules.Any(m => m != null && m.Id == id)) return;

            var module = ScriptableObject.CreateInstance<ModuleDefinition>();
            module.Initialise(id, displayName, description, SlotType.Pylon, new ModuleStats
            {
                Weight = weight,
                LiftMultiplier = 1f,
                TurnRateMultiplier = 1f,
                DashCooldownMultiplier = 1f,
            }, unlockLevel);
            module.SetWeapon(weapon);
            AssetDatabase.CreateAsset(module, $"{Folder}/{assetName}.asset");

            catalog.Initialise(catalog.Chassis.ToArray(), catalog.Modules.Append(module).ToArray());
            EditorUtility.SetDirty(catalog);
        }

        private static void SetInt(SerializedObject target, string property, int value)
        {
            var p = target.FindProperty(property);
            if (p != null) p.intValue = value;
            else Debug.LogWarning($"Setup: GhostAuthoringComponent has no '{property}' (Netcode changed?).");
        }

        private static void SetBool(SerializedObject target, string property, bool value)
        {
            var p = target.FindProperty(property);
            if (p != null) p.boolValue = value;
            else Debug.LogWarning($"Setup: GhostAuthoringComponent has no '{property}' (Netcode changed?).");
        }
    }
}
