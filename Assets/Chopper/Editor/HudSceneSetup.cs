using Chopper.Presentation.Hud;
using Chopper.UI.Hud;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Chopper.Editor
{
    /// <summary>
    /// One-click HUD setup: creates the shared panel settings (with the Chopper theme) if missing, and adds a HUD
    /// object to the open scene. Safe to run again; it reuses what is already there.
    /// </summary>
    internal static class HudSceneSetup
    {
        private const string PanelSettingsPath = "Assets/Chopper/UI/Settings/ChopperPanelSettings.asset";
        private const string ThemePath = "Assets/Chopper/UI/Theme/ChopperRuntimeTheme.tss";
        private const string HudUxmlPath = "Assets/Chopper/UI/Hud/ChopperHud.uxml";

        [MenuItem("Chopper/UI/Add HUD To Open Scene")]
        private static void AddHudToOpenScene()
        {
            var panelSettings = GetOrCreatePanelSettings();
            var hudTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(HudUxmlPath);
            if (panelSettings == null || hudTree == null)
            {
                Debug.LogError($"HUD setup failed: check {ThemePath} and {HudUxmlPath} imported without errors.");
                return;
            }

            var hud = Object.FindFirstObjectByType<ChopperHudView>();
            var go = hud != null ? hud.gameObject : new GameObject("HUD");
            if (hud == null)
                Undo.RegisterCreatedObjectUndo(go, "Add HUD");

            // No "??" here: in the editor GetComponent returns a fake-null object, not real null.
            var document = go.GetComponent<UIDocument>();
            if (document == null) document = Undo.AddComponent<UIDocument>(go);
            Undo.RecordObject(document, "Configure HUD");
            document.panelSettings = panelSettings;
            document.visualTreeAsset = hudTree;
            if (go.GetComponent<ChopperHudModelHost>() == null) Undo.AddComponent<ChopperHudModelHost>(go);
            if (go.GetComponent<ChopperHudView>() == null) Undo.AddComponent<ChopperHudView>(go);

            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeGameObject = go;
        }

        private static PanelSettings GetOrCreatePanelSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (settings != null) return settings;

            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            if (theme == null) return null;

            settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.themeStyleSheet = theme;
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PanelSettingsPath));
            AssetDatabase.CreateAsset(settings, PanelSettingsPath);
            AssetDatabase.SaveAssets();
            return settings;
        }
    }
}
