using Chopper.Presentation.Effects;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Chopper.Editor
{
    /// <summary>
    /// One-click combat effects: creates the settings asset (filled with the Sci-Fi Arsenal prefabs the old game used)
    /// if missing, and adds a Combat Effects object to the open scene. Safe to run again.
    /// </summary>
    internal static class CombatEffectsSceneSetup
    {
        private const string SettingsPath = "Assets/Chopper/Data/Effects/CombatEffectsSettings.asset";
        private const string SciFi = "Assets/AssetBundles/Archanor/Sci-Fi Arsenal/Sci-Fi Effects/Prefabs/Combat";

        private const string GunBullet = "Assets/Prefabs/Projectiles/ProjectilePrefabs/BulletRed.prefab";
        private const string GunMuzzleFlash = SciFi + "/MuzzleFlash/FireSmall/RedFireSmallMuzzleFlash.prefab";
        // The pack's original, not Prefabs/Particles/RedFireImpactClient (it holds a removed legacy script).
        private const string GunImpact = SciFi + "/Explosions/Fire/V1/RedFireImpact.prefab";
        private const string Rocket = "Assets/Prefabs/Projectiles/ProjectilePrefabs/Rocket/OrangeRocketMissile.prefab";
        private const string RocketMuzzleFlash = "Assets/Prefabs/Projectiles/ProjectilePrefabs/Rocket/RocketMuzzleFlashOrange.prefab";
        private const string RocketImpact = "Assets/Prefabs/Projectiles/ProjectilePrefabs/Rocket/RedFireRocketImpactV3.prefab";

        [MenuItem("Chopper/Effects/Add Combat Effects To Open Scene")]
        private static void AddToOpenScene()
        {
            var settings = GetOrCreateSettings();
            var host = Object.FindFirstObjectByType<CombatEffectsHost>();
            if (host == null)
            {
                var go = new GameObject("Combat Effects");
                Undo.RegisterCreatedObjectUndo(go, "Add Combat Effects");
                host = Undo.AddComponent<CombatEffectsHost>(go);
            }

            Undo.RecordObject(host, "Configure Combat Effects");
            host.SetSettings(settings);
            EditorUtility.SetDirty(host);
            EditorSceneManager.MarkSceneDirty(host.gameObject.scene);
            Selection.activeGameObject = host.gameObject;
        }

        private static CombatEffectsSettings GetOrCreateSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<CombatEffectsSettings>(SettingsPath);
            if (settings != null) return settings;

            settings = ScriptableObject.CreateInstance<CombatEffectsSettings>();
            settings.Initialise(Load(GunBullet), Load(GunMuzzleFlash), Load(GunImpact), Load(Rocket), Load(RocketMuzzleFlash), Load(RocketImpact));

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SettingsPath));
            AssetDatabase.CreateAsset(settings, SettingsPath);
            AssetDatabase.SaveAssets();
            return settings;
        }

        private static GameObject Load(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                Debug.LogWarning($"Combat effects: no prefab at {path}. Set it by hand on {SettingsPath}.");
            return prefab;
        }
    }
}
