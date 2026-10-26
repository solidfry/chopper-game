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
    /// NetCodePhysicsConfig with lag compensation, arena bounds sized to the level, and training targets.
    /// The subscene must be open for editing. Reuses what is already there.
    /// </summary>
    internal static class GameplaySceneSetup
    {
        private const string SettingsObjectName = "Gameplay Settings";
        private const string TargetPrefix = "Target";
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

        public static void Apply()
        {
            if (!TryFindGameplayScene(out var scene))
            {
                Debug.LogWarning("Setup: no open gameplay subscene (with a ChopperSpawner). Open the subscene for editing, then run step 4.");
                return;
            }

            var settings = FindOrCreateSettingsObject(scene);
            EnsureLagCompensation(scene, settings);
            EnsureArenaBounds(scene, settings);
            var targets = EnsureTrainingTargets(scene);

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

        /// <summary>Objects named Target… with a collider become damageable training targets.</summary>
        private static int EnsureTrainingTargets(Scene scene)
        {
            var added = 0;
            foreach (var collider in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Collider>(true)))
            {
                var go = collider.gameObject;
                if (!go.name.StartsWith(TargetPrefix) || go.GetComponent<TrainingTargetAuthoring>() != null) continue;
                Undo.AddComponent<TrainingTargetAuthoring>(go);
                added++;
            }
            return added;
        }

        private static Component FindInScene(Scene scene, string typeName) => scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Component>(true))
            .FirstOrDefault(c => c != null && c.GetType().Name == typeName);
    }
}
