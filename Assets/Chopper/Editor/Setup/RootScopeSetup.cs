using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chopper.App;
using UnityEditor;
using UnityEngine;
using VContainer.Unity;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// The composition root (plan section 1b): a <see cref="RootLifetimeScope"/> prefab, and the VContainer settings
    /// asset that names it, in the preloaded assets. VContainer then makes the root scope when the first scene loads,
    /// so the services (Steam, sign-in, matchmaking) start in every scene. Creates only what is missing.
    /// </summary>
    internal static class RootScopeSetup
    {
        private const string PrefabPath = "Assets/Chopper/App/RootLifetimeScope.prefab";
        private const string SettingsPath = "Assets/Chopper/Data/VContainerSettings.asset";

        public static void Ensure()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<RootLifetimeScope>(PrefabPath);
            if (prefab == null)
            {
                var go = new GameObject("RootLifetimeScope");
                try
                {
                    go.AddComponent<RootLifetimeScope>();
                    prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath).GetComponent<RootLifetimeScope>();
                }
                finally
                {
                    Object.DestroyImmediate(go);
                }
            }

            var settings = AssetDatabase.LoadAssetAtPath<VContainerSettings>(SettingsPath);
            if (settings == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                settings = ScriptableObject.CreateInstance<VContainerSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }
            if (settings.RootLifetimeScope == null)
            {
                settings.RootLifetimeScope = prefab;
                EditorUtility.SetDirty(settings);
            }

            var preloaded = PlayerSettings.GetPreloadedAssets().Where(a => a != null).ToList();
            if (!preloaded.Contains(settings))
            {
                preloaded.RemoveAll(a => a is VContainerSettings);
                preloaded.Add(settings);
                PlayerSettings.SetPreloadedAssets(preloaded.ToArray());
            }
            AssetDatabase.SaveAssets();
        }

        public static IEnumerable<string> Problems()
        {
            var settings = PlayerSettings.GetPreloadedAssets().OfType<VContainerSettings>().FirstOrDefault();
            if (settings == null)
                yield return "No VContainer settings in the preloaded assets: services (sign-in, Steam) never start. Press '1. Create / repair config'.";
            else if (settings.RootLifetimeScope is not RootLifetimeScope)
                yield return "VContainer settings do not point to the RootLifetimeScope prefab. Press '1. Create / repair config'.";
        }
    }
}
