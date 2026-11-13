using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chopper.Simulation;
using Chopper.Simulation.Combat;
using Chopper.Simulation.Session;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// Spawn system setup: the <see cref="SpawnSettings"/> asset (step 1), a first set of spawn points in a gameplay
    /// subscene that has none (step 4), the generated points of a city map (step 10), and the checks.
    /// Never moves or deletes points made by hand. Written without the editor (13 Nov 2026).
    /// </summary>
    internal static class SpawnAssets
    {
        private const string Folder = "Assets/Chopper/Data/Spawn";
        public const string SettingsPath = Folder + "/SpawnSettings.asset";
        private const string RootName = "Spawn Points";
        /// <summary>Used by Validate when the settings asset does not exist yet.</summary>
        private const int DefaultMinPoints = 8;
        /// <summary>Step 4: the first set is a ring this far round the spawner (m).</summary>
        private const float DefaultRingRadius = 150f;
        /// <summary>Step 10: points that are not over a mesa are this high over the highest mesa point (m).</summary>
        private const float RingExtraHeight = 120f;
        /// <summary>Step 10: generated points stay this far inside the flight bounds (m).</summary>
        private const float BoundsInset = 100f;

        public static SpawnSettings EnsureSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<SpawnSettings>(SettingsPath);
            if (settings != null) return settings;

            Directory.CreateDirectory(Folder);
            settings = ScriptableObject.CreateInstance<SpawnSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            AssetDatabase.SaveAssets();
            return settings;
        }

        // -- points -------------------------------------------------------------------------------------------------

        public static IEnumerable<SpawnPointAuthoring> PointsIn(Scene scene) =>
            scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<SpawnPointAuthoring>(true));

        /// <summary>
        /// Step 4. A scene with no spawn point gets the minimum count: the spawner's own place, and a ring round it at
        /// the same height, all with the spawner's facing. Move them by hand to good places. Returns the points added.
        /// </summary>
        public static int EnsurePoints(Scene scene)
        {
            if (PointsIn(scene).Any()) return 0;
            var spawner = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ChopperSpawnerAuthoring>(true)).FirstOrDefault();
            if (spawner == null) return 0;

            var count = EnsureSettings().MinPoints;
            var centre = spawner.transform.position;
            var facing = spawner.transform.rotation;
            var places = new List<(Vector3, Quaternion)> { (centre, facing) };
            for (var i = 1; i < count; i++)
            {
                var angle = 2f * Mathf.PI * (i - 1) / Mathf.Max(count - 1, 1);
                places.Add((centre + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * DefaultRingRadius, facing));
            }
            Create(scene, places, undo: true);
            return places.Count;
        }

        /// <summary>
        /// Step 10 (the scene is new). Points over the mesa tops, spread as far apart as possible, starting with
        /// <paramref name="first"/> (the spawner's place); when the map has too few mesas, more points in the widest
        /// gaps of the circle, high over the city. All face the map centre.
        /// </summary>
        public static void BuildCityPoints(Scene scene, CityAssets.TerrainData terrain, Vector3 first, float heightOverMesa)
        {
            var count = EnsureSettings().CityPoints;
            var mesas = (terrain.markers ?? Array.Empty<CityAssets.TerrainMarker>())
                .Where(m => m.kind == "MesaTop" && m.position is { Length: 3 })
                .Select(m => CityAssets.ToVector(m.position) + Vector3.up * heightOverMesa)
                .Where(p => Flat(p - first).sqrMagnitude > 1f)
                .ToList();

            var chosen = new List<Vector3> { first };
            while (chosen.Count < count && mesas.Count > 0)
            {
                // The mesa farthest from every chosen point.
                var next = mesas.OrderByDescending(m => chosen.Min(c => Flat(m - c).sqrMagnitude)).First();
                mesas.Remove(next);
                chosen.Add(next);
            }

            if (chosen.Count < count)
            {
                var half = terrain.flightBounds?.size is { Length: 3 } size
                    ? Mathf.Min(size[0], size[2]) * 0.5f - BoundsInset
                    : terrain.size * 0.5f - BoundsInset;
                var radius = Mathf.Min(chosen.Average(p => Flat(p).magnitude), half);
                if (radius < 100f) radius = Mathf.Min(terrain.size * 0.3f, half);
                var height = chosen.Max(p => p.y) + RingExtraHeight;
                var angles = chosen.Where(p => Flat(p).sqrMagnitude > 1f).Select(p => Mathf.Atan2(p.x, p.z)).ToList();
                while (chosen.Count < count)
                {
                    var angle = WidestGap(angles);
                    angles.Add(angle);
                    chosen.Add(new Vector3(Mathf.Sin(angle) * radius, height, Mathf.Cos(angle) * radius));
                }
            }

            Create(scene, chosen.Select(p => (p, FaceCentre(p))).ToList(), undo: false);
        }

        private static void Create(Scene scene, IReadOnlyList<(Vector3 Position, Quaternion Rotation)> places, bool undo)
        {
            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            if (undo) Undo.RegisterCreatedObjectUndo(root, "Add spawn points");
            for (var i = 0; i < places.Count; i++)
            {
                var go = new GameObject($"SpawnPoint_{i:00}");
                go.transform.SetParent(root.transform, false);
                go.transform.SetPositionAndRotation(places[i].Position, places[i].Rotation);
                go.AddComponent<SpawnPointAuthoring>().SetIndex(i);
            }
        }

        private static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

        private static Quaternion FaceCentre(Vector3 position)
        {
            var toCentre = Flat(-position);
            return toCentre.sqrMagnitude > 1f ? Quaternion.LookRotation(toCentre) : Quaternion.identity;
        }

        /// <summary>The middle of the widest gap between the angles (radians) on the circle.</summary>
        private static float WidestGap(List<float> angles)
        {
            if (angles.Count == 0) return 0f;
            var sorted = angles.OrderBy(a => a).ToList();
            var bestStart = sorted[^1];
            var bestGap = sorted[0] + 2f * Mathf.PI - sorted[^1];
            for (var i = 0; i < sorted.Count - 1; i++)
            {
                var gap = sorted[i + 1] - sorted[i];
                if (gap <= bestGap) continue;
                bestGap = gap;
                bestStart = sorted[i];
            }
            return Mathf.Repeat(bestStart + bestGap * 0.5f + Mathf.PI, 2f * Mathf.PI) - Mathf.PI;
        }

        // -- checks -------------------------------------------------------------------------------------------------

        /// <summary>Every map in the catalog: its gameplay scene must hold the minimum count of spawn points.</summary>
        public static IEnumerable<(string Message, Object Context)> Problems()
        {
            var config = ConfigAssets.Load();
            if (config == null || config.Maps == null) yield break;
            var minimum = config.Spawn != null ? config.Spawn.MinPoints : DefaultMinPoints;
            var script = ScriptGuid();
            if (string.IsNullOrEmpty(script)) yield break;

            foreach (var map in config.Maps.Maps)
            {
                if (map == null || string.IsNullOrEmpty(map.Scene)) continue;
                var path = GameplayScenePath(map.Scene);
                if (path == null || !File.Exists(path)) continue;
                var count = CountInSceneFile(path, script);
                if (count < 0 || count >= minimum) continue;
                var step = map.Scene.StartsWith(ChopperBootstrap.MapScenePrefix) ? "'10. City match scenes'" : "'4. Set up gameplay subscene' with the map open";
                yield return ($"Map {map.DisplayName} ({map.Scene}): {count} spawn point(s), needs {minimum}. Press {step}, or add SpawnPointAuthoring objects to {path}.",
                    AssetDatabase.LoadAssetAtPath<SceneAsset>(path));
            }
        }

        /// <summary>
        /// The open gameplay subscene: each point must be inside the arena bounds and clear of walls. The wall check
        /// sees only colliders that are loaded in the editor now (a closed city subscene is not seen); the server
        /// checks again at each spawn.
        /// </summary>
        public static void CheckScene(Scene scene, List<SetupProblem> problems)
        {
            var points = PointsIn(scene).ToArray();
            if (points.Length == 0) return;
            var settings = AssetDatabase.LoadAssetAtPath<SpawnSettings>(SettingsPath);
            var clearance = settings != null ? settings.WallClearance : SpawnRules.Default.WallClearance;
            var arena = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ArenaBoundsAuthoring>(true)).FirstOrDefault();
            var box = arena != null ? new Bounds(arena.transform.position, arena.Size) : default;
            Physics.SyncTransforms();

            foreach (var point in points)
            {
                var position = point.transform.position;
                if (arena != null && !box.Contains(position))
                    problems.Add(new SetupProblem($"Spawn point {point.name} is outside the arena bounds.", point));
                else if (Physics.CheckSphere(position, clearance, ~0, QueryTriggerInteraction.Ignore))
                    problems.Add(new SetupProblem($"Spawn point {point.name} is nearer than {clearance} m to a wall or the ground: the server will not use it.", point));
            }

            var twice = points.GroupBy(p => p.Index).Where(g => g.Count() > 1).Select(g => g.First()).FirstOrDefault();
            if (twice != null)
                problems.Add(new SetupProblem($"Spawn point index {twice.Index} is used more than once (give each point its own index).", twice));
        }

        /// <summary>The gameplay scene of a catalog map: the training range, or a city map of step 10.</summary>
        private static string GameplayScenePath(string mapScene)
        {
            if (mapScene == ChopperBootstrap.TrainingRangeScene) return MapSceneSetup.TrainingGameplayScene;
            return mapScene.StartsWith(ChopperBootstrap.MapScenePrefix)
                ? MapSceneSetup.GameplayScenePath(mapScene.Substring(ChopperBootstrap.MapScenePrefix.Length))
                : null;
        }

        private static string ScriptGuid() => AssetDatabase.FindAssets($"{nameof(SpawnPointAuthoring)} t:MonoScript")
            .FirstOrDefault(g => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g)) == nameof(SpawnPointAuthoring));

        /// <summary>Counts the components in the scene's text (the scene is not opened). -1 = not a text scene.</summary>
        private static int CountInSceneFile(string path, string scriptGuid)
        {
            var text = File.ReadAllText(path);
            if (!text.StartsWith("%YAML")) return -1;
            var needle = $"guid: {scriptGuid}";
            var count = 0;
            for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
                count++;
            return count;
        }
    }
}
