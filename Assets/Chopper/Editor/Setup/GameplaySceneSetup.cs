using System;
using System.Collections.Generic;
using System.Linq;
using Chopper.Simulation.Combat;
using Chopper.Simulation.Session;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// Sets up the gameplay subscene (the loaded scene that holds the <see cref="ChopperSpawnerAuthoring"/>):
    /// NetCodePhysicsConfig with lag compensation, arena bounds sized to the level, training targets, and the spawner's
    /// loadout catalog and missile prefab.
    /// A closed subscene is opened for the step and closed again (<see cref="WithGameplayScene"/>). Reuses what is already there.
    /// </summary>
    internal static class GameplaySceneSetup
    {
        private const string SettingsObjectName = "Gameplay Settings";
        private const string TargetPrefix = "Target";
        public const string TargetPrefabPath = "Assets/Chopper/Prefabs/TrainingTarget.prefab";
        /// <summary>Space around the level before out-of-bounds damage starts.</summary>
        private const float HorizontalMargin = 150f;
        private const float FlightCeiling = 600f;

        public static bool TryFindGameplayScene(out Scene scene)
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && scene.GetRootGameObjects().Any(r => r.GetComponentInChildren<ChopperSpawnerAuthoring>(true) != null))
                    return true;
            }
            scene = default;
            return false;
        }

        /// <summary>
        /// Runs <paramref name="action"/> on the gameplay subscene. If it is closed, opens it additively, runs the action,
        /// saves it when <paramref name="save"/> is true, and closes it again. Returns false if no gameplay subscene exists.
        /// </summary>
        public static bool WithGameplayScene(Action<Scene> action, bool save)
        {
            if (TryFindGameplayScene(out var open))
            {
                action(open);
                return true;
            }

            foreach (var path in SubScenePathsInOpenScenes())
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    if (!scene.GetRootGameObjects().Any(r => r.GetComponentInChildren<ChopperSpawnerAuthoring>(true) != null)) continue;
                    action(scene);
                    if (save && scene.isDirty) EditorSceneManager.SaveScene(scene);
                    return true;
                }
                finally
                {
                    EditorSceneManager.CloseScene(scene, removeScene: true);
                }
            }
            return false;
        }

        public static void Apply()
        {
            if (!WithGameplayScene(ApplyTo, save: true))
                Debug.LogWarning("Setup: no gameplay subscene (a SubScene whose scene has a ChopperSpawner) in the open scene.");
        }

        /// <summary>Scene paths of the SubScene components in the open scenes (found by name: no Unity.Scenes reference).</summary>
        private static string[] SubScenePathsInOpenScenes()
        {
            return Enumerable.Range(0, SceneManager.sceneCount)
                .Select(SceneManager.GetSceneAt)
                .Where(s => s.isLoaded)
                .SelectMany(s => s.GetRootGameObjects())
                .SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true))
                .Where(c => c != null && c.GetType().Name == "SubScene")
                .Select(c => c.GetType().GetProperty("SceneAsset")?.GetValue(c) as SceneAsset)
                .Where(asset => asset != null)
                .Select(AssetDatabase.GetAssetPath)
                .ToArray();
        }

        private static void ApplyTo(Scene scene)
        {
            var settings = FindOrCreateSettingsObject(scene);
            EnsureLagCompensation(scene, settings);
            EnsureArenaBounds(scene, settings);
            var targets = EnsureTrainingTargets(scene);
            EnsureSpawner(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"Setup: gameplay subscene '{scene.name}' ready ({targets} training target(s) added).");
        }

        private static GameObject FindOrCreateSettingsObject(Scene scene)
        {
            var existing = scene.GetRootGameObjects().FirstOrDefault(r => r.name == SettingsObjectName);
            if (existing != null) return existing;

            var go = new GameObject(SettingsObjectName);
            SceneManager.MoveGameObjectToScene(go, scene);
            Undo.RegisterCreatedObjectUndo(go, "Add Gameplay Settings");
            return go;
        }

        private static void EnsureLagCompensation(Scene scene, GameObject settings)
        {
            var config = FindInScene(scene, "NetCodePhysicsConfig");
            if (config == null)
            {
                // Found by name, so the editor assembly does not depend on the Netcode physics assembly.
                var type = TypeCache.GetTypesDerivedFrom<MonoBehaviour>().FirstOrDefault(t => t.Name == "NetCodePhysicsConfig");
                if (type == null)
                {
                    Debug.LogError("Setup: NetCodePhysicsConfig type not found. Is Netcode for Entities installed?");
                    return;
                }
                config = Undo.AddComponent(settings, type);
            }

            var serialized = new SerializedObject(config);
            var lagCompensation = serialized.FindProperty("EnableLagCompensation");
            if (lagCompensation != null && !lagCompensation.boolValue)
            {
                lagCompensation.boolValue = true;
                serialized.ApplyModifiedProperties();
            }
        }

        /// <summary>Box round every collider in the level, with room to fly around it.</summary>
        private static void EnsureArenaBounds(Scene scene, GameObject settings)
        {
            if (FindInScene(scene, nameof(ArenaBoundsAuthoring)) != null) return;

            var colliders = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Collider>(true)).ToArray();
            var level = colliders.Length > 0 ? colliders[0].bounds : new Bounds(Vector3.zero, Vector3.one * 1000f);
            foreach (var collider in colliders) level.Encapsulate(collider.bounds);

            var size = new Vector3(level.size.x + 2f * HorizontalMargin, FlightCeiling, level.size.z + 2f * HorizontalMargin);
            var centre = new Vector3(level.center.x, level.min.y + FlightCeiling * 0.5f - 10f, level.center.z);

            var boundsObject = new GameObject("Arena Bounds");
            Undo.RegisterCreatedObjectUndo(boundsObject, "Add Arena Bounds");
            boundsObject.transform.SetParent(settings.transform, false);
            boundsObject.transform.position = centre;
            var bounds = Undo.AddComponent<ArenaBoundsAuthoring>(boundsObject);

            var serialized = new SerializedObject(bounds);
            serialized.FindProperty("size").vector3Value = size;
            serialized.ApplyModifiedProperties();
        }

        /// <summary>The spawner needs the loadout catalog (per-player loadouts, chassis prefabs) and the missile ghost.</summary>
        private static void EnsureSpawner(Scene scene)
        {
            var config = ConfigAssets.Load();
            var missile = AssetDatabase.LoadAssetAtPath<GameObject>(GuidedWeaponAssets.MissilePrefabPath);
            foreach (var spawner in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ChopperSpawnerAuthoring>(true)))
            {
                if (spawner.Catalog != null && spawner.MissilePrefab != null) continue;
                Undo.RecordObject(spawner, "Configure chopper spawner");
                spawner.Configure(config != null ? config.LoadoutCatalog : null, missile);
                EditorUtility.SetDirty(spawner);
            }
        }

        /// <summary>
        /// Objects named Target… with a collider become damageable training targets and pre-spawned ghosts (static,
        /// interpolated), so clients see their health and the seeker lock on them. Netcode accepts a pre-spawned ghost only
        /// as the root instance of a ghost prefab, so each target is replaced by an instance of one shared
        /// <see cref="TargetPrefabPath"/> prefab (made from the first target), in the same place.
        /// </summary>
        private static int EnsureTrainingTargets(Scene scene)
        {
            var targets = TrainingTargetsIn(scene).ToList();
            if (targets.Count == 0) return 0;

            var prefab = EnsureTargetPrefab(targets[0]);
            if (prefab == null) return 0;

            var replaced = 0;
            foreach (var go in targets)
            {
                if (IsTargetPrefabInstance(go, prefab)) continue;

                var source = go.transform;
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                Undo.RegisterCreatedObjectUndo(instance, "Add training target");
                instance.name = go.name;
                instance.transform.SetPositionAndRotation(source.position, source.rotation);
                instance.transform.localScale = source.lossyScale;
                Undo.DestroyObjectImmediate(go);
                replaced++;
            }
            return replaced;
        }

        /// <summary>Objects named Target… that have a collider.</summary>
        public static IEnumerable<GameObject> TrainingTargetsIn(Scene scene) => scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Collider>(true))
            .Select(c => c.gameObject)
            .Where(go => go.name.StartsWith(TargetPrefix))
            .Distinct();

        /// <summary>True when <paramref name="go"/> is the root of an instance of the training target prefab.</summary>
        public static bool IsTargetPrefabInstance(GameObject go, GameObject prefab = null)
        {
            prefab ??= AssetDatabase.LoadAssetAtPath<GameObject>(TargetPrefabPath);
            return prefab != null && PrefabUtility.IsAnyPrefabInstanceRoot(go) &&
                   PrefabUtility.GetCorrespondingObjectFromSource(go) == prefab;
        }

        /// <summary>The shared target ghost prefab: a copy of <paramref name="template"/> (mesh, collider, material) at the origin.</summary>
        private static GameObject EnsureTargetPrefab(GameObject template)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(TargetPrefabPath);
            if (existing != null) return existing;

            var ghostType = TypeCache.GetTypesDerivedFrom<MonoBehaviour>().FirstOrDefault(t => t.Name == "GhostAuthoringComponent");
            if (ghostType == null)
            {
                Debug.LogError("Setup: GhostAuthoringComponent type not found. Is Netcode for Entities installed?");
                return null;
            }

            var copy = UnityEngine.Object.Instantiate(template);
            try
            {
                copy.name = "TrainingTarget";
                copy.transform.SetParent(null, false);
                copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                copy.transform.localScale = template.transform.lossyScale;

                if (copy.GetComponent<TrainingTargetAuthoring>() == null) copy.AddComponent<TrainingTargetAuthoring>();
                var ghostComponent = copy.GetComponent(ghostType);
                if (ghostComponent == null) ghostComponent = copy.AddComponent(ghostType);
                var ghost = new SerializedObject(ghostComponent);
                SetInt(ghost, "DefaultGhostMode", 0);    // Interpolated
                SetInt(ghost, "SupportedGhostModes", 1); // Interpolated only
                SetInt(ghost, "OptimizationMode", 1);    // Static: sent only when it changes
                ghost.ApplyModifiedPropertiesWithoutUndo();

                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(TargetPrefabPath));
                return PrefabUtility.SaveAsPrefabAsset(copy, TargetPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(copy);
            }
        }

        private static void SetInt(SerializedObject target, string property, int value)
        {
            var p = target.FindProperty(property);
            if (p != null) p.intValue = value;
        }

        private static Component FindInScene(Scene scene, string typeName) => scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Component>(true))
            .FirstOrDefault(c => c != null && c.GetType().Name == typeName);
    }
}
