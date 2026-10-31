using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chopper.App;
using Chopper.Presentation.Bridge;
using Chopper.Presentation.Config;
using Chopper.Presentation.Hangar;
using Chopper.Simulation;
using Chopper.Simulation.Loadout;
using Chopper.UI.Audio;
using Chopper.UI.Hangar;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// "Build hangar" (plan section 5, "Customisation UI"): the hangar settings and materials (style guide palette),
    /// the bloom volume, the baked wireframe lines of every chassis and module model, the hangar layout in the game
    /// config, and the hangar scene (camera, presentation bridge, turntable stage, UI) in the build settings. Creates
    /// only what is missing; new kit models get their lines on the next press.
    /// </summary>
    internal static class HangarSetup
    {
        public const string ScenePath = "Assets/Scenes/" + HangarStage.SceneName + ".unity";
        private const string Folder = "Assets/Chopper/Data/Hangar";
        private const string SettingsPath = Folder + "/HangarSettings.asset";
        private const string LibraryPath = Folder + "/WireframeLibrary.asset";
        private const string VolumePath = Folder + "/HangarVolume.asset";
        private const string LayoutPath = "Assets/Chopper/UI/Hangar/Hangar.uxml";
        private const string UnlitShader = "Universal Render Pipeline/Unlit";
        /// <summary>Lines are drawn brighter than 1 so bloom makes them glow.</summary>
        private const float Glow = 2.5f;

        // Style guide palette (UI/Theme/ChopperTheme.uss).
        private static readonly Color ConsoleGreen = new(0f, 1f, 0.58f);
        private static readonly Color ConsoleGreenDark = new(0.031f, 0.106f, 0.039f);
        private static readonly Color CruisePink = new(1f, 0.259f, 0.537f);
        private static readonly Color LightningYellow = new(1f, 0.875f, 0.42f);
        private static readonly Color ElectricBlueDark = new(0.027f, 0f, 0.098f);

        public static void Apply(GameConfig config)
        {
            if (config == null || config.LoadoutCatalog == null)
            {
                Debug.LogWarning("Setup: the hangar needs the game config and loadout catalog. Run step 1 first.");
                return;
            }

            var settings = EnsureSettings();
            BakeWireframes(settings, config.LoadoutCatalog);
            config.SetHangar(settings, AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(LayoutPath));
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            EnsureScene(config);
            EnsureBuildScenes();
        }

        public static IEnumerable<(string Message, Object Context)> Problems()
        {
            if (!File.Exists(ScenePath))
            {
                yield return ("No hangar scene. Press '7. Build hangar'.", null);
                yield break;
            }
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (!scenes.Contains(ScenePath)) yield return ("The hangar scene is not in the build settings. Press '7. Build hangar'.", null);
            if (!scenes.Any(p => Path.GetFileNameWithoutExtension(p) == ChopperBootstrap.TrainingRangeScene))
                yield return ($"{ChopperBootstrap.TrainingRangeScene} is not in the build settings: the hangar cannot launch it.", null);
            if (scenes.Length > 0 && scenes[0] != ScenePath)
                yield return ($"The first build scene is {scenes[0]}, not the hangar: the game starts there. Press '7. Build hangar'.", null);
            foreach (var path in scenes.Where(p => p != ScenePath && !IsMatchScene(p)))
                yield return ($"Old scene {path} is in the build (missing scripts). Press '7. Build hangar' to turn it off.", null);
        }

        private static HangarSettings EnsureSettings()
        {
            Directory.CreateDirectory(Folder);
            var settings = AssetDatabase.LoadAssetAtPath<HangarSettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<HangarSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }

            var library = AssetDatabase.LoadAssetAtPath<WireframeLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<WireframeLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            settings.Initialise(
                settings.Wireframes != null ? settings.Wireframes : library,
                settings.Fill != null ? settings.Fill : EnsureMaterial("HangarFill", ConsoleGreenDark),
                settings.Line != null ? settings.Line : EnsureMaterial("HangarLine", ConsoleGreen * Glow),
                settings.Highlight != null ? settings.Highlight : EnsureMaterial("HangarHighlight", CruisePink * Glow),
                settings.EmptySocket != null ? settings.EmptySocket : EnsureMaterial("HangarEmptySocket", LightningYellow * Glow),
                settings.Volume != null ? settings.Volume : EnsureVolume());
            EditorUtility.SetDirty(settings);
            return settings;
        }

        private static Material EnsureMaterial(string name, Color color)
        {
            var path = $"{Folder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find(UnlitShader)) { name = name };
            material.SetColor("_BaseColor", new Color(color.r, color.g, color.b, 1f));
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>Bloom (the neon lines glow) and a soft vignette.</summary>
        private static VolumeProfile EnsureVolume()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);
            if (profile != null) return profile;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, VolumePath);

            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.value = 0.9f;
            bloom.intensity.value = 1.4f;
            bloom.scatter.value = 0.65f;
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.value = 0.35f;
            vignette.smoothness.value = 0.5f;
            foreach (var component in profile.components)
            {
                component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            EditorUtility.SetDirty(profile);
            return profile;
        }

        /// <summary>Line meshes for every chassis model and module visual that has none yet (sub-assets of the library).</summary>
        private static void BakeWireframes(HangarSettings settings, LoadoutCatalog catalog)
        {
            var library = settings.Wireframes;
            var entries = library.Entries.Where(e => e.Source != null && e.Wire != null).ToList();
            var known = new HashSet<Mesh>(entries.Select(e => e.Source));

            var models = new List<GameObject>();
            foreach (var chassis in catalog.Chassis)
            {
                var model = chassis != null && chassis.Prefab != null ? chassis.Prefab.transform.Find(HangarStage.ModelName) : null;
                if (model != null) models.Add(model.gameObject);
            }
            models.AddRange(catalog.Modules.Where(m => m != null && m.Visual != null).Select(m => m.Visual));

            var added = 0;
            foreach (var filter in models.SelectMany(m => m.GetComponentsInChildren<MeshFilter>(true)))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null || !known.Add(mesh)) continue;
                var (points, lines) = WireframeMesh.Build(mesh.vertices, mesh.triangles, settings.CreaseDegrees, settings.LinePush);
                var wire = new Mesh { name = $"Wire_{mesh.name}" };
                if (points.Length > 65535) wire.indexFormat = IndexFormat.UInt32;
                wire.SetVertices(points);
                wire.SetIndices(lines, MeshTopology.Lines, 0);
                wire.RecalculateBounds();
                AssetDatabase.AddObjectToAsset(wire, library);
                entries.Add(new WireframeLibrary.Entry { Source = mesh, Wire = wire });
                added++;
            }

            library.SetEntries(entries.ToArray());
            EditorUtility.SetDirty(library);
            if (added > 0) Debug.Log($"Setup: baked hangar wireframe lines for {added} mesh(es).");
        }

        /// <summary>Makes the scene if missing, or repairs the objects in it; saves it. The open scenes stay as they are.</summary>
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
            camera.backgroundColor = ElectricBlueDark;
            camera.nearClipPlane = 0.1f;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;

            var volumeObject = FindOrCreate(scene, "Hangar Volume");
            var volume = volumeObject.GetComponent<Volume>();
            if (volume == null) volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = config.Hangar.Volume;

            var bridgeObject = FindOrCreate(scene, "Presentation");
            var bridge = bridgeObject.GetComponent<PresentationBridge>();
            if (bridge == null) bridge = bridgeObject.AddComponent<PresentationBridge>();
            bridge.Configure(config, null);
            bridge.SetMusic(Chopper.Presentation.Audio.MusicLibrary.Set.Menu);

            var turntable = FindOrCreate(scene, "Turntable").transform;
            var stageObject = FindOrCreate(scene, "Hangar");
            var stage = stageObject.GetComponent<HangarStage>();
            if (stage == null) stage = stageObject.AddComponent<HangarStage>();
            stage.Configure(bridge, camera, turntable);

            // Child of the root scope: hands the stage the matchmaking service (FIND MATCH).
            var scopeObject = FindOrCreate(scene, "Hangar Scope");
            var scope = scopeObject.GetComponent<HangarLifetimeScope>();
            if (scope == null) scope = scopeObject.AddComponent<HangarLifetimeScope>();

            var uiObject = FindOrCreate(scene, "Hangar UI");
            var panel = uiObject.GetComponent<PanelRenderer>();
            if (panel == null) panel = uiObject.AddComponent<PanelRenderer>();
            panel.panelSettings = config.PanelSettings;
            panel.visualTreeAsset = config.HangarLayout;
            var view = uiObject.GetComponent<HangarView>();
            if (view == null) view = uiObject.AddComponent<HangarView>();
            view.SetBridge(bridge);
            var sounds = uiObject.GetComponent<UISoundBinder>();
            if (sounds == null) sounds = uiObject.AddComponent<UISoundBinder>();
            sounds.SetBridge(bridge);

            foreach (var component in new Object[] { camera, volume, bridge, stage, scope, panel, view, sounds }) EditorUtility.SetDirty(component);
            EditorSceneManager.MarkSceneDirty(scene);
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

        /// <summary>
        /// Client build scenes: the hangar first (the game starts there), then the match scene. The old 2023 scenes stay
        /// in the list but off: their scripts are gone (missing-script warnings, wrong start scene) until they are ported.
        /// </summary>
        private static void EnsureBuildScenes()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            foreach (var scene in scenes.Skip(1)) scene.enabled = IsMatchScene(scene.path);
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static bool IsMatchScene(string path) =>
            Path.GetFileNameWithoutExtension(path) == ChopperBootstrap.TrainingRangeScene;
    }
}
