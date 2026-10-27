using System.Linq;
using Chopper.Presentation.Config;
using Chopper.Simulation.Flight;
using UnityEditor;
using UnityEngine;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// Gives the chopper ghost prefab its default loadout, the ghost settings lag compensation needs, the old
    /// chopper's look (<see cref="LegacyChopperVisuals"/>) and placeholder module sockets (<see cref="SocketAssets"/>).
    /// </summary>
    internal static class PrefabSetup
    {
        public const string ChopperPrefabPath = "Assets/Chopper/Prefabs/ChopperGhost.prefab";

        public static void Apply(GameConfig config)
        {
            var root = PrefabUtility.LoadPrefabContents(ChopperPrefabPath);
            try
            {
                var authoring = root.GetComponent<ChopperAuthoring>();
                if (authoring != null && authoring.Chassis == null && config != null && config.LoadoutCatalog != null)
                {
                    var (chassis, modules) = LoadoutAssets.DefaultLoadout(config.LoadoutCatalog);
                    authoring.SetLoadout(chassis, modules);
                }
                if (authoring != null && config != null) UpgradeDefaultLoadout(authoring, config.LoadoutCatalog);

                var ghost = FindByTypeName(root, "GhostAuthoringComponent");
                if (ghost != null)
                {
                    var serialized = new SerializedObject(ghost);
                    var track = serialized.FindProperty("TrackInterpolationDelay");
                    if (track != null && !track.boolValue)
                    {
                        track.boolValue = true;
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                    }
                }

                LegacyChopperVisuals.Apply(root);
                SocketAssets.EnsureSockets(root);
                var saved = PrefabUtility.SaveAsPrefabAsset(root, ChopperPrefabPath);
                // One ghost for every chassis until each has its own model.
                if (config != null) SocketAssets.EnsureChassis(config.LoadoutCatalog, saved);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// The first default loadout had two rocket pods. Once heat seekers exist, the second pylon gets Homing
        /// Missiles, so the default chopper can test locks, missiles and flares. Changes nothing else.
        /// </summary>
        private static void UpgradeDefaultLoadout(ChopperAuthoring authoring, Chopper.Simulation.Loadout.LoadoutCatalog catalog)
        {
            const ushort rocketPod = 111;
            var homing = catalog != null ? catalog.Modules.FirstOrDefault(m => m != null && m.Id == GuidedWeaponAssets.HomingMissilesId) : null;
            if (authoring.Chassis == null || homing == null || homing.Weapon == null) return;

            var modules = authoring.Modules.ToArray();
            if (modules.Any(m => m != null && m.Id == GuidedWeaponAssets.HomingMissilesId)) return;
            var rocketPods = modules.Select((m, i) => (m, i)).Where(x => x.m != null && x.m.Id == rocketPod).ToArray();
            if (rocketPods.Length < 2) return;

            modules[rocketPods[rocketPods.Length - 1].i] = homing;
            authoring.SetLoadout(authoring.Chassis, modules);
            Debug.Log("Setup: default loadout's second rocket pod is now Homing Missiles.");
        }

        /// <summary>Package components found by name, so this assembly does not depend on them.</summary>
        public static Component FindByTypeName(GameObject root, string typeName) =>
            root.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == typeName);
    }
}
