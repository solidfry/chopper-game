using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chopper.Presentation.Visuals;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// A scene to look at the kit and its skins: every chassis of <see cref="KitAssets.DefaultKit"/> in a row, modules
    /// fitted on their sockets (picked by slot from the kit manifest), one <see cref="SkinPreview"/> on the root that
    /// switches all of them (skin, Baked / Runtime). Not a game scene. Made once; opened when it exists.
    /// </summary>
    internal static class SkinPreviewScene
    {
        public const string ScenePath = KitAssets.KitsFolder + "/SkinPreview.unity";
        private const float Spacing = 14f;

        [Serializable]
        private sealed class Manifest
        {
            public PartInfo[] parts;
        }

        [Serializable]
        private sealed class PartInfo
        {
            public string name;
            public string file;
            public string kind;
            public string slot;
        }

        /// <summary>Setup window button: asks to save open scenes, makes the scene if missing, opens it.</summary>
        public static void Open()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!File.Exists(ScenePath)) Build();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var root = GameObject.Find("Kit Preview");
            if (root != null)
            {
                Selection.activeGameObject = root;
                SceneView.lastActiveSceneView?.FrameSelected();
            }
        }

        /// <summary>Makes and saves the scene (replaces an older one). Does not change the open scenes.</summary>
        public static void Build()
        {
            var folder = $"{KitAssets.KitsFolder}/{KitAssets.DefaultKit}";
            var manifestPath = $"{folder}/{KitAssets.DefaultKit}.manifest.json";
            if (!File.Exists(manifestPath))
            {
                Debug.LogWarning($"Setup: no kit manifest at {manifestPath}.");
                return;
            }
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));
            var library = AssetDatabase.LoadAssetAtPath<SkinLibrary>($"{folder}/{KitAssets.DefaultKit}_Skins.asset");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var previous = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
                Populate(folder, manifest, library);
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void Populate(string folder, Manifest manifest, SkinLibrary library)
        {
            var parts = manifest.parts ?? Array.Empty<PartInfo>();
            var chassis = parts.Where(p => p.kind == "chassis").ToArray();
            var modules = parts.Where(p => p.kind == "module" && !string.IsNullOrEmpty(p.slot))
                .GroupBy(p => p.slot).ToDictionary(g => g.Key, g => g.ToArray());

            var root = new GameObject("Kit Preview");
            var preview = root.AddComponent<SkinPreview>();
            var settings = new SerializedObject(preview);
            settings.FindProperty("library").objectReferenceValue = library;
            settings.FindProperty("method").enumValueIndex = (int)SkinPreview.Method.Runtime;
            settings.ApplyModifiedPropertiesWithoutUndo();

            for (var i = 0; i < chassis.Length; i++)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{folder}/{chassis[i].file}");
                if (model == null) continue;
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                instance.transform.SetParent(root.transform, false);
                instance.transform.localPosition = new Vector3((i - (chassis.Length - 1) / 2f) * Spacing, 0f, 0f);
                instance.transform.localRotation = Quaternion.Euler(0f, 150f, 0f);
                FitModules(folder, instance.transform, modules, i);
            }

            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.6f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(45f, -35f, 0f);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = new Vector3(0f, -3.2f, 0f);
            ground.transform.localScale = new Vector3(40f, 1f, 40f);

            var camera = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            camera.fieldOfView = 45f;
            camera.transform.position = new Vector3(0f, 5f, -29f);
            camera.transform.LookAt(new Vector3(0f, 0.5f, 0f));
        }

        /// <summary>A module of the socket's slot on every socket (rotor hubs all get the same rotor; the socket's scale
        /// fits it); different choppers show different modules.</summary>
        private static void FitModules(string folder, Transform chassis, Dictionary<string, PartInfo[]> modules, int chassisIndex)
        {
            foreach (var socket in chassis.GetComponentsInChildren<Transform>(true))
            {
                if (!socket.name.StartsWith("SOCKET_")) continue;
                var pieces = socket.name.Split('_');
                if (pieces.Length < 3 || !modules.TryGetValue(pieces[1], out var options)) continue;
                int.TryParse(pieces[2], out var index);
                var pick = pieces[1] == "Rotor" ? options[chassisIndex % options.Length]
                    : options[(chassisIndex + index) % options.Length];
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{folder}/{pick.file}");
                if (model == null) continue;
                var module = (GameObject)PrefabUtility.InstantiatePrefab(model);
                module.transform.SetParent(socket, false);
            }
        }
    }
}
