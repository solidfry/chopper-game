using System.Collections.Generic;
using System.Linq;
using Chopper.Presentation.Bridge;
using Chopper.Presentation.Camera;
using Chopper.Presentation.Config;
using Chopper.Simulation.Combat;
using Chopper.Simulation.Flight;
using Chopper.UI.Hud;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Chopper.Editor.Setup
{
    /// <summary>One problem found by <see cref="SetupValidator"/>. <see cref="Context"/> is pinged when clicked.</summary>
    internal readonly struct SetupProblem
    {
        public readonly string Message;
        public readonly Object Context;
        public SetupProblem(string message, Object context = null) { Message = message; Context = context; }
    }

    /// <summary>Checks config assets, the chopper prefab and the open scenes. Lists every problem in one place.</summary>
    internal static class SetupValidator
    {
        public static List<SetupProblem> Run()
        {
            var problems = new List<SetupProblem>();
            var config = ConfigAssets.Load();
            CheckConfig(config, problems);
            CheckPrefab(problems);
            CheckScene(problems);
            return problems;
        }

        private static void CheckConfig(GameConfig config, List<SetupProblem> problems)
        {
            if (config == null)
            {
                problems.Add(new SetupProblem($"No game config at {ConfigAssets.GameConfigPath}. Press 'Create / repair config'."));
                return;
            }

            var messages = new List<string>();
            config.CollectProblems(messages);
            problems.AddRange(messages.Select(m => new SetupProblem(m, config)));
        }

        private static void CheckPrefab(List<SetupProblem> problems)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabSetup.ChopperPrefabPath);
            if (prefab == null)
            {
                problems.Add(new SetupProblem($"No chopper prefab at {PrefabSetup.ChopperPrefabPath}."));
                return;
            }

            var authoring = prefab.GetComponent<ChopperAuthoring>();
            if (authoring == null || authoring.Chassis == null)
                problems.Add(new SetupProblem("Chopper prefab has no chassis / default loadout. Press 'Set up chopper prefab'.", prefab));

            var ghost = PrefabSetup.FindByTypeName(prefab, "GhostAuthoringComponent");
            var track = ghost != null ? new SerializedObject(ghost).FindProperty("TrackInterpolationDelay") : null;
            if (track != null && !track.boolValue)
                problems.Add(new SetupProblem("Chopper ghost: Track Interpolation Delay is off, so lag compensation cannot rewind.", prefab));
        }

        private static void CheckScene(List<SetupProblem> problems)
        {
            var bridge = Object.FindAnyObjectByType<PresentationBridge>();
            if (bridge == null)
            {
                problems.Add(new SetupProblem("Open scene has no Presentation bridge. Press 'Set up open scene'."));
            }
            else
            {
                if (bridge.Config == null) problems.Add(new SetupProblem("Presentation bridge has no game config.", bridge));
                var rig = Object.FindAnyObjectByType<ChopperCameraRig>(FindObjectsInactive.Include);
                if (rig == null)
                    problems.Add(new SetupProblem("Open scene has no ChopperCameraRig (chase camera). Press '3. Set up open scene'.", bridge));
                else if (!rig.isActiveAndEnabled)
                    problems.Add(new SetupProblem("ChopperCameraRig is on a disabled object or component.", rig));
                else if (bridge.CameraRig != rig)
                    problems.Add(new SetupProblem("Presentation bridge does not point to the chase camera. Press '3. Set up open scene'.", bridge));
            }

            if (Object.FindAnyObjectByType<ChopperHudView>() == null)
                problems.Add(new SetupProblem("Open scene has no HUD. Press 'Set up open scene'."));

            // Gameplay content lives in the subscene; it is only visible here while the subscene is open.
            if (!GameplaySceneSetup.TryFindGameplayScene(out _))
            {
                problems.Add(new SetupProblem("Gameplay subscene is closed (or has no ChopperSpawner): open it to check its contents."));
                return;
            }

            var physicsConfig = FindInLoadedScenes("NetCodePhysicsConfig");
            if (physicsConfig == null)
            {
                problems.Add(new SetupProblem("No NetCodePhysicsConfig in the gameplay subscene. Press '4. Set up gameplay subscene'."));
            }
            else
            {
                var lagCompensation = new SerializedObject(physicsConfig).FindProperty("EnableLagCompensation");
                if (lagCompensation != null && !lagCompensation.boolValue)
                    problems.Add(new SetupProblem("NetCodePhysicsConfig: Enable Lag Compensation is off.", physicsConfig));
            }

            if (Object.FindAnyObjectByType<ArenaBoundsAuthoring>() == null)
                problems.Add(new SetupProblem("No arena bounds in the gameplay subscene. Press '4. Set up gameplay subscene'."));
            if (Object.FindAnyObjectByType<TrainingTargetAuthoring>() == null)
                problems.Add(new SetupProblem("No training targets in the gameplay subscene (objects named Target… get them in step 4)."));
        }

        private static Component FindInLoadedScenes(string typeName)
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    var found = root.GetComponentsInChildren<Component>(true)
                        .FirstOrDefault(c => c != null && c.GetType().Name == typeName);
                    if (found != null) return found;
                }
            }
            return null;
        }
    }
}
