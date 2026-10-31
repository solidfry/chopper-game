using System.IO;
using Chopper.Simulation;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// "Build Linux server" (plan section 4, Edgegap): a Linux dedicated server (headless) with the gameplay scene only,
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
                    scenes = new[] { GameplayScene },
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
