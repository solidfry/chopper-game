using System.IO;
using Chopper.Presentation.Effects;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// Weapon review scene (owner, 27 Nov): the effects bench stage, a stand-in gunship hovering at one end and a target
    /// block at the other, and a <see cref="WeaponReview"/> that fires every weapon module with its family look and
    /// sounds (Docs/WeaponFeelPlan.md). Not a game scene and not in the build. Rebuilt by the Setup button each time, so
    /// it always lists the current catalog.
    /// </summary>
    internal static class WeaponReviewScene
    {
        public const string ScenePath = "Assets/Chopper/Scenes/WeaponReview.unity";

        /// <summary>Setup window button: asks to save open scenes, rebuilds the scene, opens it.</summary>
        public static void Open()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("Weapon review: stop play mode first.");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var review = Object.FindAnyObjectByType<WeaponReview>();
            if (review != null) Selection.activeGameObject = review.gameObject;
        }

        public static void Build()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var previous = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            try
            {
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
                Populate();
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void Populate()
        {
            // The camera looks at the middle of the range: shooter at -Z, target at +Z.
            var focus = new GameObject("View Focus").transform;
            focus.position = new Vector3(0f, 6f, -5f);
            EffectsBenchScene.Stage(focus);

            var target = new GameObject("Target").transform;
            target.position = new Vector3(0f, 2f, 30f);
            EffectsBenchScene.Block("Target Block", new Vector3(0f, 4f, 34f), new Vector3(10f, 8f, 4f));

            var shooter = EffectsBenchScene.StandIn();
            // Nose at the target, as when you aim in a match.
            var from = new Vector3(0f, 14f, -40f);
            shooter.SetPositionAndRotation(from, Quaternion.LookRotation(target.position - from));

            var config = ConfigAssets.Load();
            var review = new GameObject("Weapon Review").AddComponent<WeaponReview>();
            review.Configure(config != null ? config.Effects : null, config != null ? config.Audio : null,
                config != null ? config.LoadoutCatalog : null, shooter, target,
                AssetDatabase.LoadAssetAtPath<Chopper.Simulation.Loadout.ChassisDefinition>("Assets/Chopper/Data/Loadout/Chassis_Gunship.asset"));

            // Start the camera from the side of the range.
            var camera = UnityEngine.Camera.main;
            if (camera != null)
                camera.transform.SetPositionAndRotation(new Vector3(-45f, 22f, -20f), Quaternion.LookRotation(focus.position - new Vector3(-45f, 22f, -20f)));
        }
    }
}
