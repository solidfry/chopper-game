using Chopper.Presentation.Effects;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// Combat effects settings, filled with the Sci-Fi Arsenal prefabs the old game used. Creates it if missing.
    /// </summary>
    internal static class EffectsAssets
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

        private const string Missile = SciFi + "/Missiles/Rockets/RedRocketMissile.prefab";
        private const string Flare = SciFi + "/Missiles/Sparkler/YellowSparklerMissile.prefab";

        public static CombatEffectsSettings Ensure(AudioMixer mixer)
        {
            var settings = AssetDatabase.LoadAssetAtPath<CombatEffectsSettings>(SettingsPath);
            if (settings != null)
            {
                // Repair: fill slots added after the asset was made (guided missiles, flares).
                settings.FillGuidedSlots(Load(Missile), Load(RocketImpact), Load(Flare));
                EditorUtility.SetDirty(settings);
                // Repair: route prefab sounds once the mixer exists.
                if (settings.EffectsMixerGroup == null && mixer != null)
                {
                    settings.SetEffectsMixerGroup(AudioAssets.Group(mixer, "Effects"));
                    EditorUtility.SetDirty(settings);
                }
                return settings;
            }

            settings = ScriptableObject.CreateInstance<CombatEffectsSettings>();
            settings.Initialise(Load(GunBullet), Load(GunMuzzleFlash), Load(GunImpact), Load(Rocket), Load(RocketMuzzleFlash), Load(RocketImpact));
            settings.FillGuidedSlots(Load(Missile), Load(RocketImpact), Load(Flare));
            if (mixer != null) settings.SetEffectsMixerGroup(AudioAssets.Group(mixer, "Effects"));

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
