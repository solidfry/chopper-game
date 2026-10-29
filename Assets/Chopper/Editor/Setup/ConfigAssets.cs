using Chopper.Presentation.Config;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// Creates or repairs every config asset and links them into the <see cref="GameConfig"/> root.
    /// Creates only what is missing; never overwrites tuned values.
    /// </summary>
    internal static class ConfigAssets
    {
        public const string GameConfigPath = "Assets/Chopper/Data/GameConfig.asset";
        private const string PanelSettingsPath = "Assets/Chopper/UI/Settings/ChopperPanelSettings.asset";
        private const string ThemePath = "Assets/Chopper/UI/Theme/ChopperRuntimeTheme.tss";
        private const string HudLayoutPath = "Assets/Chopper/UI/Hud/ChopperHud.uxml";
        private const string OptionsLayoutPath = "Assets/Chopper/UI/Options/OptionsMenu.uxml";
        private const string CameraSettingsPath = "Assets/Chopper/Data/Camera/CameraSettings.asset";
        private const string SpeedLineShaderPath = "Assets/Chopper/Presentation/Camera/SpeedLine.shader";

        public static GameConfig Load() => AssetDatabase.LoadAssetAtPath<GameConfig>(GameConfigPath);

        public static GameConfig EnsureAll()
        {
            RootScopeSetup.Ensure();
            NetcodeSettingsSetup.Ensure();
            var mixer = AudioAssets.LoadMixer();
            var catalog = LoadoutAssets.Ensure();
            GuidedWeaponAssets.Ensure(catalog);
            SocketAssets.EnsureModuleVisuals(catalog);
            var library = AudioAssets.Ensure(mixer);
            var effects = EffectsAssets.Ensure(mixer);
            var panel = EnsurePanelSettings();
            var look = HudLookAssets.Ensure();
            var cameraSettings = EnsureCameraSettings();
            var hud = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(HudLayoutPath);
            var options = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(OptionsLayoutPath);
            var music = AudioAssets.EnsureMusic();

            var config = Load();
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<GameConfig>();
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(GameConfigPath));
                AssetDatabase.CreateAsset(config, GameConfigPath);
                config.Initialise(catalog, library, effects, music, panel, hud, look, options, cameraSettings);
            }
            else
            {
                // Repair: fill only the empty slots.
                config.Initialise(
                    config.LoadoutCatalog != null ? config.LoadoutCatalog : catalog,
                    config.Audio != null ? config.Audio : library,
                    config.Effects != null ? config.Effects : effects,
                    config.Music != null ? config.Music : music,
                    config.PanelSettings != null ? config.PanelSettings : panel,
                    config.HudLayout != null ? config.HudLayout : hud,
                    config.HudLook != null ? config.HudLook : look,
                    config.OptionsLayout != null ? config.OptionsLayout : options,
                    config.Camera != null ? config.Camera : cameraSettings);
            }

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            return config;
        }

        /// <summary>Camera views and feel. Defaults: the tuned chase view and the old game's cockpit camera.</summary>
        private static Chopper.Presentation.Camera.CameraSettings EnsureCameraSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<Chopper.Presentation.Camera.CameraSettings>(CameraSettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<Chopper.Presentation.Camera.CameraSettings>();
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(CameraSettingsPath));
                AssetDatabase.CreateAsset(settings, CameraSettingsPath);
            }
            if (settings.SpeedLineShader == null)
            {
                settings.SetSpeedLineShader(AssetDatabase.LoadAssetAtPath<Shader>(SpeedLineShaderPath));
                EditorUtility.SetDirty(settings);
            }
            return settings;
        }

        private static PanelSettings EnsurePanelSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (settings != null) return settings;

            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            if (theme == null)
            {
                Debug.LogError($"Setup: no theme at {ThemePath}.");
                return null;
            }

            settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.themeStyleSheet = theme;
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PanelSettingsPath));
            AssetDatabase.CreateAsset(settings, PanelSettingsPath);
            return settings;
        }
    }
}
