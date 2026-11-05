using System.IO;
using System.Linq;
using Chopper.Simulation;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// "Build Linux server" (plan section 4, Edgegap): a Linux dedicated server (headless): the boot scene
    /// (<see cref="BootScene"/>, picks the match map: <c>-map</c> or random from the map catalog's match pool) and every
    /// match-pool map scene (no catalog: the training range only),
    /// to <see cref="OutputFolder"/>, plus the Dockerfile from <c>Tools/Edgegap</c>. Needs the Unity Hub module "Linux
    /// Dedicated Server Build Support". The server makes only a server world and listens on port 7979
    /// (<see cref="ChopperBootstrap"/>).
    /// Entities builds its content only for the active platform, and switching to Linux Server changes the script
    /// defines (<c>UNITY_SERVER</c>), so the build runs in steps: switch, let the editor recompile and reload, build
    /// (<see cref="Resume"/>), switch back. The result is in the console and in <see cref="ResultFile"/>.
    /// Unity 6.6: Build Profiles → Linux Server → "Build and Run on" must not be "Local Machine" on Windows (set
    /// RemoteSSH; no device needed), or the build fails at once with 0 errors (the Entities content build refuses).
    /// </summary>
    internal static class ServerBuild
    {
        public const string OutputFolder = "Builds/LinuxServer";
        public const string ResultFile = "Builds/server_build_result.txt";
        private const string Executable = "AirwingServer.x86_64";
        private const string Dockerfile = "Tools/Edgegap/Dockerfile";
        private const string GameplayScene = "Assets/Scenes/" + ChopperBootstrap.TrainingRangeScene + ".unity";
        public const string BootScene = "Assets/Scenes/ServerBoot.unity";
        private const string PendingKey = "Chopper.ServerBuild.Pending";
        private const string ReturnTargetKey = "Chopper.ServerBuild.ReturnTarget";
        private const string ReturnSubtargetKey = "Chopper.ServerBuild.ReturnSubtarget";

        public static void BuildLinux()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneLinux64))
            {
                EditorUtility.DisplayDialog("Build Linux server",
                    "The Linux Dedicated Server module is not installed. Unity Hub → Installs → this editor → Add modules.", "OK");
                return;
            }
            if (EditorUtility.DisplayDialog("Build Linux server",
                    $"Build the dedicated server to {OutputFolder}? This takes several minutes: the editor switches to Linux " +
                    "Server (scripts recompile), builds, and switches back.", "Build", "Cancel"))
                Start();
        }

        /// <summary>Starts the steps (also for scripts and batch mode). Builds at once when the editor is on Linux Server already.</summary>
        public static void Start()
        {
            if (IsOnLinuxServer())
            {
                BuildNow();
                return;
            }

            SessionState.SetBool(PendingKey, true);
            SessionState.SetInt(ReturnTargetKey, (int)EditorUserBuildSettings.activeBuildTarget);
            SessionState.SetInt(ReturnSubtargetKey, (int)EditorUserBuildSettings.standaloneBuildSubtarget);
            Debug.Log("Server build: switching to Linux Server; the build starts after the scripts reload.");
            EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Server;
            EditorUserBuildSettings.SwitchActiveBuildTarget(NamedBuildTarget.Server, BuildTarget.StandaloneLinux64);
            // If the switch needs no reload, carry on here; else Resume runs after the reload.
            EditorApplication.delayCall += ContinueWhenReady;
        }

        [InitializeOnLoadMethod]
        private static void Resume()
        {
            if (SessionState.GetBool(PendingKey, false)) EditorApplication.delayCall += ContinueWhenReady;
        }

        private static void ContinueWhenReady()
        {
            if (!SessionState.GetBool(PendingKey, false)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || !IsOnLinuxServer())
            {
                EditorApplication.delayCall += ContinueWhenReady;
                return;
            }

            SessionState.EraseBool(PendingKey);
            try
            {
                BuildNow();
            }
            finally
            {
                var target = (BuildTarget)SessionState.GetInt(ReturnTargetKey, (int)BuildTarget.StandaloneWindows64);
                EditorUserBuildSettings.standaloneBuildSubtarget =
                    (StandaloneBuildSubtarget)SessionState.GetInt(ReturnSubtargetKey, (int)StandaloneBuildSubtarget.Player);
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildPipeline.GetBuildTargetGroup(target), target);
            }
        }

        /// <summary>The boot scene and the match maps; just the training range without a map catalog.</summary>
        private static string[] ServerScenes()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<Chopper.Simulation.Session.MapCatalog>(MapSceneSetup.CatalogPath);
            if (catalog == null) return new[] { GameplayScene };
            var scenes = new System.Collections.Generic.List<string> { EnsureBootScene(catalog) };
            foreach (var map in catalog.Maps)
            {
                if (map == null || !map.InMatchPool) continue;
                var path = AssetDatabase.FindAssets($"t:Scene {map.Scene}")
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == map.Scene);
                if (path != null) scenes.Add(path);
                else Debug.LogWarning($"Server build: map scene {map.Scene} not found; the server cannot run it.");
            }
            Debug.Log($"Server build: scenes {string.Join(", ", scenes)}.");
            return scenes.ToArray();
        }

        /// <summary>The server's first scene: only a <see cref="Chopper.Simulation.Session.ServerMapLoader"/>.</summary>
        private static string EnsureBootScene(Chopper.Simulation.Session.MapCatalog catalog)
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Additive);
            var loader = new GameObject("Server Map Loader").AddComponent<Chopper.Simulation.Session.ServerMapLoader>();
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(loader.gameObject, scene);
            loader.SetCatalog(catalog);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, BootScene);
            UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, removeScene: true);
            return BootScene;
        }

        private static bool IsOnLinuxServer() =>
            EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneLinux64 &&
            EditorUserBuildSettings.standaloneBuildSubtarget == StandaloneBuildSubtarget.Server;

        private static void BuildNow()
        {
            Directory.CreateDirectory(OutputFolder);
            // Entities makes a folder named after the product; "Airwing: Nitro" has a ':' that Windows paths cannot hold.
            // The server never shows its name, so build it without the colon and put the name back after.
            var productName = PlayerSettings.productName;
            BuildReport report;
            try
            {
                PlayerSettings.productName = productName.Replace(":", "");
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = ServerScenes(),
                    locationPathName = Path.Combine(OutputFolder, Executable),
                    target = BuildTarget.StandaloneLinux64,
                    subtarget = (int)StandaloneBuildSubtarget.Server,
                    options = BuildOptions.None,
                });
            }
            finally
            {
                // Saved: the build wrote the changed name to ProjectSettings.asset.
                PlayerSettings.productName = productName;
                // The build leaves an empty entry in the preloaded assets; drop it.
                var preloaded = new System.Collections.Generic.List<Object>(PlayerSettings.GetPreloadedAssets());
                if (preloaded.RemoveAll(a => a == null) > 0) PlayerSettings.SetPreloadedAssets(preloaded.ToArray());
                AssetDatabase.SaveAssets();
            }

            var summary = report.summary;
            var result = $"{summary.result}: {summary.totalErrors} error(s), {summary.totalSize / (1024f * 1024f):0} MB, {summary.totalTime.TotalSeconds:0} s";
            File.WriteAllText(ResultFile, result);
            if (summary.result == BuildResult.Succeeded)
            {
                File.Copy(Dockerfile, Path.Combine(OutputFolder, "Dockerfile"), overwrite: true);
                File.Copy(Dockerfile + "/../.dockerignore", Path.Combine(OutputFolder, ".dockerignore"), overwrite: true);
                Debug.Log($"Server build: {result} → {summary.outputPath}. Dockerfile copied; see {Dockerfile} for the push steps.");
            }
            else
            {
                Debug.LogError($"Server build {result}. See the errors above.");
            }
        }
    }
}
