using System.Linq;
using Chopper.Presentation.Config;
using Chopper.Simulation.Flight;
using UnityEditor;
using UnityEngine;

namespace Chopper.Editor.Setup
{
    /// <summary>Gives the chopper ghost prefab its default loadout and the ghost settings lag compensation needs.</summary>
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

                PrefabUtility.SaveAsPrefabAsset(root, ChopperPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>Package components found by name, so this assembly does not depend on them.</summary>
        public static Component FindByTypeName(GameObject root, string typeName) =>
            root.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == typeName);
    }
}
