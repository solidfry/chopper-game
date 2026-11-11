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
using UnityEngine.Rendering;
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
        /// <summary>
        /// Simplified city flown over behind the menu (<see cref="HomeFlyover"/>), made from <see cref="SourceCityPath"/>:
        /// buildings and the ground near them only. It must be in the build.
        /// </summary>
        public const string BackdropScenePath = "Assets/Scenes/HomeCity.unity";
        private const string SourceCityPath = "Assets/Scenes/City04.unity";
        /// <summary>Side of the flat menu floor, metres.</summary>
        private const float GroundSize = 16000f;
        private const string VolumePath = "Assets/Chopper/Data/Rendering/HomeTerminal.asset";
        private const string TracerPath = "Assets/Chopper/Data/Rendering/HomeTracer.mat";
        private const string FocusVolumePath = "Assets/Chopper/Data/Rendering/HomeFocus.asset";
        private const string WirePath = "Assets/Chopper/Data/Rendering/HomeCityWire.mat";
        private const string GroundWirePath = "Assets/Chopper/Data/Rendering/HomeGroundWire.mat";
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
            BuildBackdropCity();
            EnsureScene(config);
            EnsureBuildScenes();
            Debug.Log($"Setup: home screen built ({ScenePath}), first in the build list.");
        }

        /// <summary>
        /// (Re)builds the menu city from the City04 art scene: the buildings (prefab links kept) and the ground chunks
        /// (no crater: a flat floor instead), without colliders or authoring scripts; no backdrop, lights, smoke, traffic
        /// or markers. Every renderer gets the terminal wireframe material (<c>Chopper/TerminalWire</c>). Skipped when the
        /// source scene is missing.
        /// </summary>
        private static void BuildBackdropCity()
        {
            if (!File.Exists(SourceCityPath))
            {
                Debug.LogWarning($"Setup: no {SourceCityPath}; the home screen keeps its drawn grid. Press step 8 first.");
                return;
            }
            var wasOpen = SceneManager.GetSceneByPath(SourceCityPath).isLoaded;
            var source = wasOpen ? SceneManager.GetSceneByPath(SourceCityPath) : EditorSceneManager.OpenScene(SourceCityPath, OpenSceneMode.Additive);
            var target = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var buildings = source.GetRootGameObjects().FirstOrDefault(g => g.name == "Buildings");
                if (buildings == null)
                {
                    Debug.LogWarning($"Setup: {SourceCityPath} has no Buildings root.");
                    return;
                }
                CopyChildren(buildings, target, _ => true, EnsureWireMaterial(WirePath, ground: false), keepColliders: false);

                // No crater: one flat wire-grid floor at the buildings' base height, wide enough for the sprawl copies
                // HomeFlyover adds round the core at run time.
                var baseHeight = buildings.GetComponentsInChildren<Renderer>().Select(r => r.bounds.min.y).DefaultIfEmpty(0f).Min();
                var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                floor.name = "Ground";
                SceneManager.MoveGameObjectToScene(floor, target);
                floor.transform.SetPositionAndRotation(new Vector3(0f, baseHeight - 0.5f, 0f), Quaternion.identity);
                floor.transform.localScale = new Vector3(GroundSize / 10f, 1f, GroundSize / 10f);
                floor.GetComponent<Renderer>().sharedMaterial = EnsureWireMaterial(GroundWirePath, ground: true);

                EditorSceneManager.SaveScene(target, BackdropScenePath);
            }
            finally
            {
                EditorSceneManager.CloseScene(target, true);
                if (!wasOpen) EditorSceneManager.CloseScene(source, true);
            }
        }

        /// <summary>
        /// A terminal wireframe material (<c>Chopper/TerminalWire</c>). The ground one is dimmer with a coarser grid and
        /// no windows, so the towers stand out. Made once; tune the assets.
        /// </summary>
        private static Material EnsureWireMaterial(string path, bool ground)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            var shader = Shader.Find("Chopper/TerminalWire");
            if (shader == null)
            {
                Debug.LogWarning("Setup: shader Chopper/TerminalWire is missing; the menu city keeps its own materials.");
                return null;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            material = new Material(shader) { enableInstancing = true };
            if (ground)
            {
                material.SetColor("_LineColour", new Color(0.1f, 0.7f, 0.4f));
                material.SetFloat("_GridSize", 12f);
                material.SetFloat("_MinorStrength", 0.1f);
                material.SetFloat("_RimStrength", 0.15f);
                material.SetFloat("_WindowChance", 0f);
                material.SetFloat("_ScanStrength", 0f);
            }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>Copies a root's children (prefab links kept) under a same-named root, then strips colliders and scripts.</summary>

        private static void CopyChildren(GameObject root, Scene target, System.Func<GameObject, bool> keep, Material wire, bool keepColliders)
        {
            var copy = new GameObject(root.name);
            SceneManager.MoveGameObjectToScene(copy, target);
            foreach (Transform child in root.transform)
            {
                if (!keep(child.gameObject)) continue;
                var prefab = PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject);
                var made = prefab != null && PrefabUtility.IsOutermostPrefabInstanceRoot(child.gameObject)
                    ? (GameObject)PrefabUtility.InstantiatePrefab(prefab, target)
                    : Object.Instantiate(child.gameObject);
                made.name = child.name;
                made.transform.SetParent(copy.transform, false);
                made.transform.SetLocalPositionAndRotation(child.localPosition, child.localRotation);
                made.transform.localScale = child.localScale;
                // The ground keeps its colliders: the menu camera tests them to stay clear of the crater walls.
                if (!keepColliders)
                    foreach (var collider in made.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
                foreach (var script in made.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(script);
                if (wire == null) continue;
                foreach (var renderer in made.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterials = Enumerable.Repeat(wire, renderer.sharedMaterials.Length).ToArray();
            }
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
            camera.nearClipPlane = 1f;
            camera.farClipPlane = 20000f;
            camera.fieldOfView = 50f;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;

            var bridgeObject = FindOrCreate(scene, "Presentation");
            var bridge = bridgeObject.GetComponent<PresentationBridge>();
            if (bridge == null) bridge = bridgeObject.AddComponent<PresentationBridge>();
            bridge.Configure(config, null);
            bridge.SetMusic(Chopper.Presentation.Audio.MusicLibrary.Set.Menu);

            // Live map behind the menu, in the terminal look.
            var flyover = camera.GetComponent<HomeFlyover>();
            if (flyover == null) flyover = camera.gameObject.AddComponent<HomeFlyover>();
            flyover.SetBridge(bridge);
            flyover.SetScene(Path.GetFileNameWithoutExtension(BackdropScenePath));
            var volumeObject = FindOrCreate(scene, "Home Volume");
            var volume = volumeObject.GetComponent<Volume>();
            if (volume == null) volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = EnsureVolume();
            // Submenus: blur and darken the city (the flyover fades this one in).
            var focusObject = FindOrCreate(scene, "Home Focus Volume");
            var focus = focusObject.GetComponent<Volume>();
            if (focus == null) focus = focusObject.AddComponent<Volume>();
            focus.isGlobal = true;
            focus.priority = 1f;
            focus.weight = 0f;
            focus.sharedProfile = EnsureFocusVolume();
            flyover.SetFocusVolume(focus);
            flyover.SetQuantizeShader(Shader.Find("Hidden/Chopper/PixelQuantize"));
            // Choppers dogfighting over the city.
            var skirmish = camera.GetComponent<HomeSkirmish>();
            if (skirmish == null) skirmish = camera.gameObject.AddComponent<HomeSkirmish>();
            skirmish.Configure(bridge, flyover, AssetDatabase.LoadAssetAtPath<Material>(WirePath), EnsureTracerMaterial());
            flyover.SetSkirmish(skirmish);
            EditorUtility.SetDirty(skirmish);
            EditorUtility.SetDirty(flyover);
            EditorUtility.SetDirty(volume);
            EditorUtility.SetDirty(focus);

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

        /// <summary>
        /// The terminal look: no colour, a green tint, high contrast, bloom on the city lights, grain, a dark vignette and
        /// a little colour fringe. Made once; tune the asset.
        /// </summary>
        private static VolumeProfile EnsureVolume()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);
            // The first version tinted before desaturating (URP desaturates last, so it came out grey): remake it.
            if (profile != null && profile.Has<ChannelMixer>()) return profile;
            if (profile != null) AssetDatabase.DeleteAsset(VolumePath);
            Directory.CreateDirectory(Path.GetDirectoryName(VolumePath));
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, VolumePath);

            // Green monochrome: every output channel is the brightness (0.3 R + 0.59 G + 0.11 B), scaled to the
            // console green (0.35, 1, 0.62). Saturation stays 0, so the tint survives.
            var mixer = profile.Add<ChannelMixer>(true);
            SetMix(mixer.redOutRedIn, mixer.redOutGreenIn, mixer.redOutBlueIn, 0.35f);
            SetMix(mixer.greenOutRedIn, mixer.greenOutGreenIn, mixer.greenOutBlueIn, 1f);
            SetMix(mixer.blueOutRedIn, mixer.blueOutGreenIn, mixer.blueOutBlueIn, 0.62f);
            var colour = profile.Add<ColorAdjustments>(true);
            colour.contrast.value = 50f;
            colour.postExposure.value = 0f;
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.value = 0.7f;
            bloom.intensity.value = 1.6f;
            bloom.scatter.value = 0.7f;
            bloom.tint.value = new Color(0.5f, 1f, 0.75f);
            var grain = profile.Add<FilmGrain>(true);
            grain.intensity.value = 0.45f;
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.value = 0.45f;
            vignette.smoothness.value = 0.45f;
            vignette.color.value = new Color(0f, 0.04f, 0.02f);
            var fringe = profile.Add<ChromaticAberration>(true);
            fringe.intensity.value = 0.2f;
            foreach (var component in profile.components)
            {
                component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            EditorUtility.SetDirty(profile);
            return profile;
        }

        /// <summary>Submenu focus: everything blurred (Gaussian depth of field from 0 m) and darker. Only exposure is
        /// overridden in the colour step, so the terminal look underneath stays. Made once; tune the asset.</summary>
        private static VolumeProfile EnsureFocusVolume()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(FocusVolumePath);
            if (profile != null) return profile;
            Directory.CreateDirectory(Path.GetDirectoryName(FocusVolumePath));
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, FocusVolumePath);

            var blur = profile.Add<DepthOfField>(true);
            blur.mode.value = DepthOfFieldMode.Gaussian;
            blur.gaussianStart.value = 0f;
            blur.gaussianEnd.value = 1f;
            blur.gaussianMaxRadius.value = 1.5f;
            blur.highQualitySampling.value = true;
            var colour = profile.Add<ColorAdjustments>();
            colour.postExposure.Override(-2.2f);
            foreach (var component in profile.components)
            {
                component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            EditorUtility.SetDirty(profile);
            return profile;
        }

        /// <summary>Menu tracers: URP Unlit, a bright (above 1) green so bloom makes them glow. Made once.</summary>
        private static Material EnsureTracerMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(TracerPath);
            if (material != null) return material;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) return null;
            Directory.CreateDirectory(Path.GetDirectoryName(TracerPath));
            material = new Material(shader);
            material.SetColor("_BaseColor", new Color(1.5f, 6f, 3f, 1f));
            AssetDatabase.CreateAsset(material, TracerPath);
            return material;
        }

        /// <summary>One output channel = brightness × <paramref name="scale"/> (mixer values are percent).</summary>
        private static void SetMix(ClampedFloatParameter red, ClampedFloatParameter green, ClampedFloatParameter blue, float scale)
        {
            red.value = 30f * scale;
            green.value = 59f * scale;
            blue.value = 11f * scale;
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
            // The home backdrop city loads by name, so it must be in the build. (The full City04 art scene was the
            // backdrop for a short time on 11 Nov; it does not belong in the build.)
            scenes.RemoveAll(s => s.path == SourceCityPath);
            if (File.Exists(BackdropScenePath))
            {
                scenes.RemoveAll(s => s.path == BackdropScenePath);
                scenes.Add(new EditorBuildSettingsScene(BackdropScenePath, true));
            }
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
