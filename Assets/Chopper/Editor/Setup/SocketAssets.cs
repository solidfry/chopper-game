using System.Collections.Generic;
using System.Linq;
using Chopper.Simulation.Loadout;
using UnityEditor;
using UnityEngine;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// Modular build ("lego" sockets) placeholders: socket children on the chopper ghost, each chassis's prefab and
    /// default modules, and module visuals from the PolygonMilitary helicopter attachments. Creates or fills only what
    /// is missing; never moves a socket or replaces a chosen visual.
    /// </summary>
    internal static class SocketAssets
    {
        private const string SocketsObjectName = "Sockets";
        private const string ArtFolder = "Assets/AssetBundles/PolygonMilitary/Prefabs/Vehicles/Veh_Attachments";

        /// <summary>
        /// One socket per slot type and index any chassis uses, so one ghost can show every chassis until each has its
        /// own model. Nose and pylons come from the old chopper's weapon slots (pylons at the rocket pod height);
        /// the rest are rough guesses: move them in the prefab. Pylon 0 is on the right, as the weapons' default
        /// launch offsets expect.
        /// </summary>
        private static readonly (SlotType Slot, int Index, Vector3 Position, float Yaw)[] Layout =
        {
            (SlotType.Nose, 0, new Vector3(0f, -1.9f, 3f), 180f),
            (SlotType.Pylon, 0, new Vector3(1.45f, -1.2f, -0.625f), -90f),
            (SlotType.Pylon, 1, new Vector3(-1.45f, -1.2f, -0.625f), 90f),
            (SlotType.Turret, 0, new Vector3(0f, -1.7f, 1.6f), 180f),
            (SlotType.Utility, 0, new Vector3(0.6f, -1.3f, -2.4f), -45f),
            (SlotType.Utility, 1, new Vector3(-0.6f, -1.3f, -2.4f), 45f),
            (SlotType.Winch, 0, new Vector3(0f, -1.8f, 0f), 0f),
            (SlotType.Rotor, 0, new Vector3(0f, 1.2f, 0f), 0f),
            (SlotType.Engine, 0, new Vector3(0f, 0.6f, -1.5f), 0f),
            (SlotType.Armour, 0, Vector3.zero, 0f),
        };

        /// <summary>Default module per slot type, by index (second pylon gets missiles). No entry = empty slot.</summary>
        private static readonly Dictionary<SlotType, ushort[]> DefaultsBySlot = new()
        {
            { SlotType.Nose, new ushort[] { 101 } },
            { SlotType.Pylon, new ushort[] { 111, GuidedWeaponAssets.HomingMissilesId } },
            { SlotType.Turret, new ushort[] { 121 } },
            { SlotType.Utility, new ushort[] { 131 } },
            { SlotType.Winch, new ushort[] { 141 } },
            { SlotType.Rotor, new ushort[] { 151 } },
            { SlotType.Engine, new ushort[] { 161 } },
            { SlotType.Armour, new ushort[] { 171 } },
        };

        /// <summary>Placeholder art. Not the old Prefabs/Weapons prefabs: they carry the old NGO Weapon script (missing).</summary>
        private static readonly (ushort[] Modules, string Prefab)[] Visuals =
        {
            (new ushort[] { 101, 102, 103 }, "SM_Veh_Heli_Attach_Minigun"),
            (new ushort[] { 111 }, "SM_Veh_Heli_Attach_Rocket_Pod_02"),
            (new ushort[] { GuidedWeaponAssets.HomingMissilesId, GuidedWeaponAssets.GroundSeekerId, GuidedWeaponAssets.ClusterMissilesId },
                "SM_Veh_Heli_Attach_Missle_01"),
            (new ushort[] { 131 }, "SM_Veh_Attach_Flare_01"),
            (new ushort[] { 141 }, "SM_Veh_Attach_Winch_01"),
        };

        /// <summary>Adds the missing sockets under a "Sockets" child of the opened ghost prefab. Returns how many.</summary>
        public static int EnsureSockets(GameObject ghostRoot)
        {
            var existing = new HashSet<(SlotType, int)>(
                ghostRoot.GetComponentsInChildren<SocketAuthoring>(true).Select(s => (s.Slot, s.Index)));

            var parent = ghostRoot.transform.Find(SocketsObjectName);
            var added = 0;
            foreach (var (slot, index, position, yaw) in Layout)
            {
                if (existing.Contains((slot, index))) continue;
                if (parent == null)
                {
                    parent = new GameObject(SocketsObjectName).transform;
                    parent.SetParent(ghostRoot.transform, false);
                }

                var socket = new GameObject($"{slot} {index}");
                socket.transform.SetParent(parent, false);
                socket.transform.localPosition = position;
                socket.AddComponent<SocketAuthoring>().Initialise(slot, index, yaw);
                added++;
            }

            if (added > 0) Debug.Log($"Setup: added {added} placeholder socket(s) to the chopper ghost. Move them to fit the model.");
            return added;
        }

        /// <summary>Gives each chassis the ghost prefab (if it has none) and a default loadout (if it has none).</summary>
        public static void EnsureChassis(LoadoutCatalog catalog, GameObject ghostPrefab)
        {
            if (catalog == null) return;
            foreach (var chassis in catalog.Chassis)
            {
                if (chassis == null) continue;
                var needsPrefab = chassis.Prefab == null;
                var needsDefaults = chassis.DefaultModules.Count != chassis.Slots.Count;
                if (!needsPrefab && !needsDefaults) continue;

                chassis.SetDefaults(needsPrefab ? ghostPrefab : null, needsDefaults ? DefaultModules(catalog, chassis) : null);
                EditorUtility.SetDirty(chassis);
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>Links placeholder art to modules that have no visual.</summary>
        public static void EnsureModuleVisuals(LoadoutCatalog catalog)
        {
            if (catalog == null) return;
            var linked = 0;
            foreach (var (ids, prefabName) in Visuals)
            {
                var art = AssetDatabase.LoadAssetAtPath<GameObject>($"{ArtFolder}/{prefabName}.prefab");
                if (art == null)
                {
                    Debug.LogWarning($"Setup: no {prefabName} in {ArtFolder}; those modules stay without a visual.");
                    continue;
                }

                foreach (var module in catalog.Modules.Where(m => m != null && m.Visual == null && ids.Contains(m.Id)))
                {
                    module.SetVisual(art);
                    EditorUtility.SetDirty(module);
                    linked++;
                }
            }

            if (linked == 0) return;
            AssetDatabase.SaveAssets();
            Debug.Log($"Setup: linked placeholder art to {linked} module(s).");
        }

        private static ModuleDefinition[] DefaultModules(LoadoutCatalog catalog, ChassisDefinition chassis)
        {
            var ordinals = new Dictionary<SlotType, int>();
            var modules = new ModuleDefinition[chassis.Slots.Count];
            for (var slot = 0; slot < modules.Length; slot++)
            {
                var slotType = chassis.Slots[slot];
                ordinals.TryGetValue(slotType, out var ordinal);
                ordinals[slotType] = ordinal + 1;

                if (!DefaultsBySlot.TryGetValue(slotType, out var ids) || ordinal >= ids.Length) continue;
                modules[slot] = catalog.Modules.FirstOrDefault(m => m != null && m.Id == ids[ordinal]);
            }
            return modules;
        }

        /// <summary>Chassis slots that have no matching socket on the chassis prefab, as "Pylon 1" names.</summary>
        public static IEnumerable<string> MissingSockets(ChassisDefinition chassis)
        {
            if (chassis == null || chassis.Prefab == null) yield break;
            var sockets = new HashSet<(SlotType, int)>(
                chassis.Prefab.GetComponentsInChildren<SocketAuthoring>(true).Select(s => (s.Slot, s.Index)));

            var ordinals = new Dictionary<SlotType, int>();
            foreach (var slotType in chassis.Slots)
            {
                ordinals.TryGetValue(slotType, out var ordinal);
                ordinals[slotType] = ordinal + 1;
                if (!sockets.Contains((slotType, ordinal))) yield return $"{slotType} {ordinal}";
            }
        }
    }
}
