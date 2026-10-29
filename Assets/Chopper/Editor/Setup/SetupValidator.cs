using System.Collections.Generic;
using System.Linq;
using Chopper.Presentation.Bridge;
using Chopper.Presentation.Camera;
using Chopper.Presentation.Config;
using Chopper.Presentation.Hud;
using Chopper.Simulation.Combat;
using Chopper.Simulation.Flight;
using Chopper.UI.Hud;
using Chopper.UI.Options;
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
            problems.AddRange(RootScopeSetup.Problems().Select(m => new SetupProblem(m)));
            CheckHudLook(problems);
            problems.AddRange(KitAssets.Problems().Select(p => new SetupProblem(p.Message, p.Context)));
            problems.AddRange(KitChassis.Problems(config).Select(p => new SetupProblem(p.Message, p.Context)));
            problems.AddRange(HangarSetup.Problems().Select(p => new SetupProblem(p.Message, p.Context)));
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

        /// <summary>Every URP renderer needs the HUD look feature with its shader, or the look presets show nothing.</summary>
        private static void CheckHudLook(List<SetupProblem> problems)
        {
            foreach (var renderer in HudLookAssets.Renderers())
            {
                var feature = HudLookAssets.FindFeature(renderer);
                if (feature == null)
                    problems.Add(new SetupProblem($"URP renderer {renderer.name} has no HUD look feature. Press '1. Create / repair config'.", renderer));
                else if (feature.Shader == null)
                    problems.Add(new SetupProblem($"URP renderer {renderer.name}: HUD look feature has no shader ({HudLookRendererFeature.ShaderName}).", renderer));
                else if (!feature.isActive)
                    problems.Add(new SetupProblem($"URP renderer {renderer.name}: HUD look feature is turned off.", renderer));
            }
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

            var visuals = prefab.GetComponent<Chopper.Simulation.Visuals.ChopperVisualsAuthoring>();
            if (visuals == null || visuals.Rotors.Length == 0)
                problems.Add(new SetupProblem("Chopper prefab has no spinning rotors. Press 'Set up chopper prefab' (copies the old chopper's look).", prefab));

            var ghost = PrefabSetup.FindByTypeName(prefab, "GhostAuthoringComponent");
            var track = ghost != null ? new SerializedObject(ghost).FindProperty("TrackInterpolationDelay") : null;
            if (track != null && !track.boolValue)
                problems.Add(new SetupProblem("Chopper ghost: Track Interpolation Delay is off, so lag compensation cannot rewind.", prefab));

            CheckModularBuild(ConfigAssets.Load(), problems);
        }

        /// <summary>Every chassis slot needs a socket on its prefab; weapon and utility modules should have a visual.</summary>
        private static void CheckModularBuild(GameConfig config, List<SetupProblem> problems)
        {
            var catalog = config != null ? config.LoadoutCatalog : null;
            if (catalog == null) return;

            foreach (var chassis in catalog.Chassis.Where(c => c != null && c.Prefab != null))
            {
                var missing = SocketAssets.MissingSockets(chassis).ToArray();
                if (missing.Length > 0)
                    problems.Add(new SetupProblem(
                        $"{chassis.DisplayName}: prefab {chassis.Prefab.name} has no socket for {string.Join(", ", missing)}. " +
                        "Press '6. Build chassis from kit' (kit model) or '2. Set up chopper prefab' (placeholder).",
                        chassis.Prefab));
            }

            var unseen = catalog.Modules.Where(m => m != null && m.Visual == null && (m.Weapon != null || m.Ability != null)).ToArray();
            if (unseen.Length > 0)
                problems.Add(new SetupProblem(
                    $"{unseen.Length} weapon/utility module(s) have no visual, so they do not show on the chopper: " +
                    string.Join(", ", unseen.Select(m => m.DisplayName)) + ".", unseen[0]));
        }

        private static void CheckScene(List<SetupProblem> problems)
        {
            // The hangar is a menu scene: no chase camera, HUD or gameplay subscene.
            if (Object.FindAnyObjectByType<Chopper.Presentation.Hangar.HangarStage>() != null) return;

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
            if (Object.FindAnyObjectByType<OptionsMenuView>(FindObjectsInactive.Include) == null)
                problems.Add(new SetupProblem("Open scene has no options overlay (Escape menu). Press '3. Set up open scene'."));

            // A UIDocument next to a PanelRenderer builds the same layout twice: the UI shows double.
            foreach (var panel in Object.FindObjectsByType<UnityEngine.UIElements.PanelRenderer>(FindObjectsInactive.Include))
                if (panel.GetComponent<UnityEngine.UIElements.UIDocument>() != null)
                    problems.Add(new SetupProblem($"{panel.name} has a PanelRenderer and an old UIDocument, so its UI shows twice. Press '3. Set up open scene'.", panel));

            // Gameplay content lives in the subscene; a closed one is opened for the check and closed again.
            if (!GameplaySceneSetup.WithGameplayScene(scene => CheckGameplayScene(scene, problems), save: false))
                problems.Add(new SetupProblem("No gameplay subscene found (a SubScene whose scene has a ChopperSpawner)."));
        }

        private static void CheckGameplayScene(Scene scene, List<SetupProblem> problems)
        {
            var physicsConfig = FindInScene(scene, "NetCodePhysicsConfig");
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

            if (FindInScene(scene, nameof(ArenaBoundsAuthoring)) == null)
                problems.Add(new SetupProblem("No arena bounds in the gameplay subscene. Press '4. Set up gameplay subscene'."));
            var spawner = FindInScene(scene, nameof(Chopper.Simulation.Session.ChopperSpawnerAuthoring)) as Chopper.Simulation.Session.ChopperSpawnerAuthoring;
            if (spawner != null && (spawner.Catalog == null || spawner.MissilePrefab == null))
                problems.Add(new SetupProblem("Chopper spawner has no loadout catalog or missile prefab. Press '4. Set up gameplay subscene'.", spawner));
            if (FindInScene(scene, nameof(TrainingTargetAuthoring)) == null)
                problems.Add(new SetupProblem("No training targets in the gameplay subscene (objects named Target… get them in step 4)."));
            // Netcode: a pre-spawned ghost must be the root instance of a ghost prefab, or the subscene does not bake.
            var loose = GameplaySceneSetup.TrainingTargetsIn(scene).Where(go => !GameplaySceneSetup.IsTargetPrefabInstance(go)).ToArray();
            if (loose.Length > 0)
                problems.Add(new SetupProblem(
                    $"{loose.Length} training target(s) are not instances of {GameplaySceneSetup.TargetPrefabPath}, so they cannot be ghosts. Press '4. Set up gameplay subscene'.",
                    loose[0]));
        }

        private static Component FindInScene(Scene scene, string typeName) => scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Component>(true))
            .FirstOrDefault(c => c != null && c.GetType().Name == typeName);
    }
}
