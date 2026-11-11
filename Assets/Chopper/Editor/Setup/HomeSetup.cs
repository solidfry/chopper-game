using System.IO;
using System.Linq;
using Chopper.Presentation.Bridge;
using Chopper.Presentation.Config;
using Chopper.Presentation.Menu;
using Chopper.UI.Audio;
using Chopper.UI.Home;
using Chopper.UI.Options;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// Step 11 "Build home screen": the main menu scene <c>Assets/Scenes/Home.unity</c> (camera, presentation bridge
    /// with menu music, the home panel with <see cref="HomeView"/>, the options overlay opened by OPTIONS) and makes it
    /// the first build scene, the hangar second. Safe to run again: it repairs what is missing.
    /// The old UGUI <c>MainMenu</c> scene is not touched (it goes in the clean-up step).
    /// </summary>
    internal static class HomeSetup
    {
        public const string ScenePath = "Assets/Scenes/" + HomeModel.SceneName + ".unity";
        private const string LayoutPath = "Assets/Chopper/UI/Home/Home.uxml";
        private static readonly Color Background = new(0.008f, 0.031f, 0.016f);

        public static void Apply(GameConfig config)
        {
            if (config == null || config.LoadoutCatalog == null)
            {
                Debug.LogWarning("Setup: the home screen needs the game config and loadout catalog. Run step 1 first.");
                return;
            }
            var layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(LayoutPath);
            if (layout == null)
            {
                Debug.LogWarning($"Setup: no home layout at {LayoutPath}.");
                return;
            }
            config.SetHome(layout);
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            EnsureScene(config);
            EnsureBuildScenes();
            Debug.Log($"Setup: home screen built ({ScenePath}), first in the build list.");
        }

        private static void EnsureScene(GameConfig config)
        {
            var open = SceneManager.GetSceneByPath(ScenePath);
            var wasOpen = open.IsValid() && open.isLoaded;
            Scene scene;
            if (wasOpen) scene = open;
            else if (File.Exists(ScenePath)) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            else scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            var previous = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(scene);
            try
            {
                Populate(scene, config);
            }
            finally
            {
                if (previous.IsValid() && previous != scene) SceneManager.SetActiveScene(previous);
            }
            EditorSceneManager.SaveScene(scene, ScenePath);
            if (!wasOpen) EditorSceneManager.CloseScene(scene, true);
        }

        private static void Populate(Scene scene, GameConfig config)
        {
            var camera = FindOrCreate(scene, "Main Camera").GetComponent<Camera>();
            if (camera == null)
            {
                camera = Find(scene, "Main Camera").AddComponent<Camera>();
                camera.gameObject.AddComponent<AudioListener>();
            }
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Background;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;

            var bridgeObject = FindOrCreate(scene, "Presentation");
            var bridge = bridgeObject.GetComponent<PresentationBridge>();
            if (bridge == null) bridge = bridgeObject.AddComponent<PresentationBridge>();
            bridge.Configure(config, null);
            bridge.SetMusic(Chopper.Presentation.Audio.MusicLibrary.Set.Menu);

            // Options overlay over the home panel; the home view opens and closes it (no Escape toggle of its own).
            var optionsObject = FindOrCreate(scene, "Options");
            var optionsPanel = Panel(optionsObject, config.PanelSettings, config.OptionsLayout, 100);
            var options = optionsObject.GetComponent<OptionsMenuView>();
            if (options == null) options = optionsObject.AddComponent<OptionsMenuView>();
            options.SetBridge(bridge);
            options.SetToggleWithEscape(false);
            var optionsSounds = Sounds(optionsObject, bridge);

            var uiObject = FindOrCreate(scene, "Home UI");
            var panel = Panel(uiObject, config.PanelSettings, config.HomeLayout, 0);
            var view = uiObject.GetComponent<HomeView>();
            if (view == null) view = uiObject.AddComponent<HomeView>();
            view.Configure(bridge, options);
            var sounds = Sounds(uiObject, bridge);

            foreach (var component in new Object[] { camera, bridge, optionsPanel, options, optionsSounds, panel, view, sounds })
                EditorUtility.SetDirty(component);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static PanelRenderer Panel(GameObject go, PanelSettings settings, VisualTreeAsset layout, int sortingOrder)
        {
            var panel = go.GetComponent<PanelRenderer>();
            if (panel == null) panel = go.AddComponent<PanelRenderer>();
            panel.panelSettings = settings;
            panel.visualTreeAsset = layout;
            panel.sortingOrder = sortingOrder;
            return panel;
        }

        private static UISoundBinder Sounds(GameObject go, PresentationBridge bridge)
        {
            var sounds = go.GetComponent<UISoundBinder>();
            if (sounds == null) sounds = go.AddComponent<UISoundBinder>();
            sounds.SetBridge(bridge);
            return sounds;
        }

        private static GameObject Find(Scene scene, string name) => scene.GetRootGameObjects().FirstOrDefault(g => g.name == name);

        private static GameObject FindOrCreate(Scene scene, string name)
        {
            var found = Find(scene, name);
            if (found != null) return found;
            var created = new GameObject(name);
            SceneManager.MoveGameObjectToScene(created, scene);
            return created;
        }

        /// <summary>Home first (the game starts there), the hangar second; the rest keep their order and state.</summary>
        internal static void EnsureBuildScenes()
        {
            if (!File.Exists(ScenePath)) return;
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
