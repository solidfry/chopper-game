using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// "Build Windows client (test)": a development build (services use the development environment) of the scenes
    /// enabled in the build settings, to <see cref="OutputFolder"/>. The editor must be on Windows (Entities builds its
    /// content only for the active platform). The result is in the console and in <see cref="ResultFile"/>.
    /// Test windows on one PC (each needs its own <c>-profile</c>, so each is a different player):
    /// <c>AirwingNitro.exe -profile p2 -bot -autoMatch -loadLog -logFile p2.log -screen-width 960 -screen-height 540 -screen-fullscreen 0</c>.
    /// <c>-join IP:PORT</c> instead of <c>-autoMatch</c> goes straight to one server.
    /// </summary>
    internal static class ClientBuild
    {
        public const string OutputFolder = "Builds/Windows";
        public const string ResultFile = "Builds/client_build_result.txt";
        private const string Executable = "AirwingNitro.exe";

        public static void BuildWindows()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
            {
                EditorUtility.DisplayDialog("Build Windows client",
                    "Switch the editor to Windows first (File → Build Profiles → Windows → Switch Platform).", "OK");
                return;
            }
            Build();
        }

        /// <summary>Builds without dialogs (also for scripts). True when the build succeeded.</summary>
        public static bool Build()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            Directory.CreateDirectory(OutputFolder);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(OutputFolder, Executable),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development,
            });

            var summary = report.summary;
            var result = $"{summary.result}: {summary.totalErrors} error(s), {summary.totalSize / (1024f * 1024f):0} MB, " +
                         $"{summary.totalTime.TotalSeconds:0} s, scenes {string.Join(", ", scenes.Select(Path.GetFileNameWithoutExtension))}";
            File.WriteAllText(ResultFile, result);
            if (summary.result == BuildResult.Succeeded) Debug.Log($"Client build: {result} → {summary.outputPath}");
            else Debug.LogError($"Client build {result}. See the errors above.");
            return summary.result == BuildResult.Succeeded;
        }
    }
}
