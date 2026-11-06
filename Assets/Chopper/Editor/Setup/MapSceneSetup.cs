using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chopper.Presentation.Config;
using Chopper.Presentation.Visuals;
using Chopper.Simulation;
using Chopper.Simulation.Combat;
using Chopper.Simulation.Session;
using Chopper.Simulation.Visuals;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// Setup step 10, city match scenes. For each exported city (step 8) builds <c>Assets/Scenes/Map_&lt;kit&gt;.unity</c>:
    /// the presentation bridge, HUD and camera rig (same as step 3), a gameplay SubScene (spawner over a mesa,
    /// arena bounds from the map's flight bounds, netcode physics settings), the city SubScene, the sun, ambient
    /// light and background of the map's time of day (<see cref="EnvironmentSettings"/>), the city post volume (step 9),
    /// and reflection probes baked under that light. Adds the scene to the build settings.
    /// Time of day and weather: only the default (<see cref="DefaultTime"/>, <see cref="DefaultWeather"/>) is built now;
    /// the other looks are planned (see <see cref="EnvironmentSettings"/>). Probe cubemaps are kept per time of day.
    /// The scene is rebuilt each time (it holds nothing made by hand). Written with the editor open (5 Nov 2026).
    /// </summary>
    internal static class MapSceneSetup
    {
        public const TimeOfDay DefaultTime = TimeOfDay.Night;
        public const Weather DefaultWeather = Weather.Clear;
        private const string EnvironmentPath = "Assets/Chopper/Data/Rendering/EnvironmentSettings.asset";
        public const string CatalogPath = "Assets/Chopper/Data/Maps/MapCatalog.asset";
        private const string TrainingGameplayScene = "Assets/Scenes/TrainingRange/TrainingRange_Gameplay.unity";
        private const string SubSceneType = "Unity.Scenes.SubScene, Unity.Scenes";
        /// <summary>Spawn this high over the chosen mesa top (m).</summary>
        private const float SpawnHeight = 30f;
        private const float CameraFarClip = 20000f;

        public static string ScenePath(string kit) => $"{CityAssets.SceneFolder}/{ChopperBootstrap.MapScenePrefix}{kit}.unity";
        public static string GameplayScenePath(string kit) =>
            $"{CityAssets.SceneFolder}/{ChopperBootstrap.MapScenePrefix}{kit}/{ChopperBootstrap.MapScenePrefix}{kit}_Gameplay.unity";
        public static string ProbePath(string kit, TimeOfDay time, string probe) =>
            $"Assets/Chopper/Art/City/{kit}/Probes/{time}/{probe}.exr";

        public static void Apply(GameConfig config)
        {
            if (config == null)
            {
                Debug.LogWarning("Setup: no game config. Press '1. Create / repair config' first.");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var environment = EnsureEnvironment(config);
            var catalog = EnsureCatalog(config);
            var built = 0;
            var returnTo = SceneManager.GetActiveScene().path;
            foreach (var map in CityAssets.Maps())
            {
                if (map.Terrain == null || !File.Exists(CityAssets.ScenePath(map.Kit)))
                {
                    Debug.LogWarning($"Setup: city {map.Kit} has no city scene. Press '8. Import city (CityForge)' first.");
                    continue;
                }
                // Load again each time: opening a scene can unload assets, which leaves an old handle dead.
                catalog = AssetDatabase.LoadAssetAtPath<MapCatalog>(CatalogPath);
                environment = AssetDatabase.LoadAssetAtPath<EnvironmentSettings>(EnvironmentPath);
                var entry = catalog.Add(Path.GetFileNameWithoutExtension(ScenePath(map.Kit)), map.Kit, inTraining: true, inMatchPool: true);
                var mapId = entry.Id;
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssetIfDirty(catalog);
                Build(config, environment, map.Kit, map.Terrain, mapId);
                built++;
            }
            if (!string.IsNullOrEmpty(returnTo) && File.Exists(returnTo)) EditorSceneManager.OpenScene(returnTo, OpenSceneMode.Single);
            AssetDatabase.SaveAssets();
            Debug.Log($"Setup: {built} city match scene(s) built ({DefaultTime}, {DefaultWeather}).");
        }

        public static IEnumerable<(string Message, Object Context)> Problems()
        {
            var config = ConfigAssets.Load();
            if (config != null && config.Environment == null)
                yield return ("No environment settings (time of day, weather). Press '10. City match scenes'.", null);
            if (config != null && config.Maps == null)
                yield return ("No map catalog (training maps, match pool). Press '10. City match scenes'.", null);
            var inBuild = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToHashSet();
            foreach (var map in CityAssets.Maps())
            {
                if (map.Terrain == null) continue;
                var path = ScenePath(map.Kit);
                if (!File.Exists(path)) yield return ($"City {map.Kit}: no match scene. Press '10. City match scenes'.", null);
                else if (!inBuild.Contains(path)) yield return ($"{path} is not on in the build settings. Press '10. City match scenes'.", null);
            }
        }

        // -- environment looks ------------------------------------------------------------------------------------

        /// <summary>Made once with the looks that exist now (Night, Clear), then left alone for tuning.</summary>
        private static EnvironmentSettings EnsureEnvironment(GameConfig config)
        {
            var settings = AssetDatabase.LoadAssetAtPath<EnvironmentSettings>(EnvironmentPath);
            if (settings == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(EnvironmentPath));
                settings = ScriptableObject.CreateInstance<EnvironmentSettings>();
                // Night: moonlight from the south-east, cool ambient, warm haze from the city below.
                settings.Initialise(new EnvironmentSettings.TimeOfDayLook { Time = TimeOfDay.Night },
                    new EnvironmentSettings.WeatherLook { Weather = Weather.Clear });
                AssetDatabase.CreateAsset(settings, EnvironmentPath);
            }
            if (config.Environment != settings)
            {
                config.SetEnvironment(settings);
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssetIfDirty(config);
            }
            return settings;
        }

        // -- map catalog ------------------------------------------------------------------------------------------

        /// <summary>The map list: the training range first (training only: not a match map), then each city map as
        /// it is built (training and match pool). Linked to the GameConfig. Hand edits (names, pool) stay.</summary>
        internal static MapCatalog EnsureCatalog(GameConfig config)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<MapCatalog>(CatalogPath);
            if (catalog == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath));
                catalog = ScriptableObject.CreateInstance<MapCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            var training = catalog.Add(ChopperBootstrap.TrainingRangeScene, "Training Range", inTraining: true, inMatchPool: false);
            var trainingId = training.Id;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
            EnsureMapIdentity(TrainingGameplayScene, trainingId);
            catalog = AssetDatabase.LoadAssetAtPath<MapCatalog>(CatalogPath);
            if (config != null && config.Maps != catalog)
            {
                config.SetMaps(catalog);
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssetIfDirty(config);
            }
            return catalog;
        }

        /// <summary>A <see cref="MatchMapAuthoring"/> with the map id on the scene's spawner (or a new object).</summary>
        private static void EnsureMapIdentity(string scenePath, byte id)
        {
            if (!File.Exists(scenePath)) return;
            var open = SceneManager.GetSceneByPath(scenePath);
            var scene = open.isLoaded ? open : EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                var identity = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MatchMapAuthoring>(true)).FirstOrDefault();
                if (identity != null && identity.MapId == id) return;
                if (identity == null)
                {
                    var spawner = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ChopperSpawnerAuthoring>(true)).FirstOrDefault();
                    var host = spawner != null ? spawner.gameObject : new GameObject("Map Identity");
                    if (spawner == null) SceneManager.MoveGameObjectToScene(host, scene);
                    identity = host.AddComponent<MatchMapAuthoring>();
                }
                identity.Set(id);
                EditorUtility.SetDirty(identity);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (!open.isLoaded) EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }

        // -- scenes -------------------------------------------------------------------------------------------------

        private static void Build(GameConfig config, EnvironmentSettings environment, string kit, CityAssets.TerrainData terrain, byte mapId)
        {
            var gameplayPath = BuildGameplayScene(kit, terrain, mapId);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath(kit));
            AddSubScene(scene, "Gameplay (SubScene)", gameplayPath);
            AddSubScene(scene, "City (SubScene)", CityAssets.ScenePath(kit));

            // Load again: opening a scene can unload assets, and an old handle gave the HUD no panel settings or layout.
            config = ConfigAssets.Load();
            SceneSetup.Apply(config);                        // bridge, HUD, options, cockpit screens, camera rig
            var look = environment.Look(DefaultTime);
            var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>(true)).FirstOrDefault();
            if (camera != null)
            {
                camera.farClipPlane = CameraFarClip;         // the backdrop and skyline sit far out
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = look.Background;
                EditorUtility.SetDirty(camera);
            }
            ApplyLook(scene, look);
            CityLookSetup.EnsureVolume(CityLookSetup.EnsureProfile());
            EditorSceneManager.SaveScene(scene);

            BakeProbes(kit, scene);
            AddToBuild(ScenePath(kit));
        }

        /// <summary>The map's gameplay subscene: rebuilt each time (only generated content).</summary>
        private static string BuildGameplayScene(string kit, CityAssets.TerrainData terrain, byte mapId)
        {
            var path = GameplayScenePath(kit);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var spawn = SpawnPoint(terrain, out var facing);
                var spawner = new GameObject("ChopperSpawner");
                SceneManager.MoveGameObjectToScene(spawner, scene);
                spawner.transform.SetPositionAndRotation(spawn, facing);
                spawner.AddComponent<ChopperSpawnerAuthoring>();
                // Which map this is: the server tells online clients (MatchMapAnnounceSystem).
                spawner.AddComponent<MatchMapAuthoring>().Set(mapId);

                var settings = new GameObject("Gameplay Settings");
                SceneManager.MoveGameObjectToScene(settings, scene);
                var bounds = new GameObject("Arena Bounds");
                bounds.transform.SetParent(settings.transform, false);
                var box = terrain.flightBounds;
                bounds.transform.position = box?.centre != null ? CityAssets.ToVector(box.centre) : new Vector3(0f, 450f, 0f);
                var arena = bounds.AddComponent<ArenaBoundsAuthoring>();
                var serialized = new SerializedObject(arena);
                serialized.FindProperty("size").vector3Value = box?.size != null ? CityAssets.ToVector(box.size) : new Vector3(2600f, 900f, 2600f);
                serialized.ApplyModifiedPropertiesWithoutUndo();

                EditorSceneManager.SaveScene(scene, path);   // named first: the setup log uses the scene name
                GameplaySceneSetup.ApplyTo(scene);           // netcode physics settings, lag compensation, spawner config
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, removeScene: true);
            }
            return path;
        }

        /// <summary>Over the mesa top nearest the city centre, facing the centre (open air, clear of the towers).</summary>
        private static Vector3 SpawnPoint(CityAssets.TerrainData terrain, out Quaternion facing)
        {
            var mesa = (terrain.markers ?? Array.Empty<CityAssets.TerrainMarker>())
                .Where(m => m.kind == "MesaTop" && m.position is { Length: 3 })
                .Select(m => CityAssets.ToVector(m.position))
                .OrderBy(p => new Vector2(p.x, p.z).sqrMagnitude)
                .DefaultIfEmpty(new Vector3(0f, 150f, -900f))
                .First();
            var position = mesa + Vector3.up * SpawnHeight;
            var toCentre = new Vector3(-position.x, 0f, -position.z);
            facing = toCentre.sqrMagnitude > 1f ? Quaternion.LookRotation(toCentre) : Quaternion.identity;
            return position;
        }

        private static void AddSubScene(Scene scene, string name, string scenePath)
        {
            var type = Type.GetType(SubSceneType);
            var asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
            if (type == null || asset == null)
            {
                Debug.LogWarning($"Setup: cannot add SubScene '{scenePath}' (Entities SubScene type or scene missing).");
                return;
            }
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            var component = go.AddComponent(type);
            type.GetProperty("SceneAsset")?.SetValue(component, asset);
            EditorUtility.SetDirty(component);
        }

        /// <summary>Sun (or moon), ambient trilight and no sky box: the parts of the look that are built now.</summary>
        private static void ApplyLook(Scene scene, EnvironmentSettings.TimeOfDayLook look)
        {
            var sunObject = new GameObject("Sun");
            SceneManager.MoveGameObjectToScene(sunObject, scene);
            sunObject.transform.rotation = Quaternion.Euler(look.SunPitchYaw.x, look.SunPitchYaw.y, 0f);
            var sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = look.SunColour;
            sun.intensity = look.SunIntensity;
            sun.shadows = LightShadows.Soft;

            RenderSettings.sun = sun;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = look.AmbientSky;
            RenderSettings.ambientEquatorColor = look.AmbientEquator;
            RenderSettings.ambientGroundColor = look.AmbientGround;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;   // no sky: reflections come from the probes
            RenderSettings.fog = false;                                            // the height fog pass does the fog
            EditorSceneManager.MarkSceneDirty(scene);
        }

        /// <summary>
        /// Bakes the city's probes under this scene's light into <see cref="ProbePath"/> (one set per time of day) and
        /// sets them as custom probes, so the bake survives a new city import (step 8 reads the files back).
        /// </summary>
        private static void BakeProbes(string kit, Scene host)
        {
            var city = EditorSceneManager.OpenScene(CityAssets.ScenePath(kit), OpenSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(host);            // the bake uses the active scene's ambient light
                var probes = city.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ReflectionProbe>(true)).ToArray();
                // Plumes in edit mode are empty; the lights and emissive windows are what the probes should see.
                foreach (var probe in probes)
                {
                    var path = ProbePath(kit, DefaultTime, probe.name);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    probe.mode = ReflectionProbeMode.Baked;
                    if (!Lightmapping.BakeReflectionProbe(probe, path))
                    {
                        Debug.LogWarning($"Setup: could not bake reflection probe {probe.name} of {kit}.");
                        continue;
                    }
                    AssetDatabase.ImportAsset(path);
                    probe.mode = ReflectionProbeMode.Custom;
                    probe.customBakedTexture = AssetDatabase.LoadAssetAtPath<Texture>(path);
                    EditorUtility.SetDirty(probe);
                }
                EditorSceneManager.MarkSceneDirty(city);
                EditorSceneManager.SaveScene(city);
                Debug.Log($"Setup: baked {probes.Length} reflection probes of {kit} ({DefaultTime}).");
            }
            finally
            {
                EditorSceneManager.CloseScene(city, removeScene: true);
            }
        }

        private static void AddToBuild(string path)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            var listed = scenes.FirstOrDefault(s => s.path == path);
            if (listed != null && listed.enabled) return;
            if (listed != null) listed.enabled = true;          // listed but off: training could not load it
            else scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
