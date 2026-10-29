using Chopper.Presentation.Bridge;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// Dedicated server builds (<see cref="ServerBuild"/>): removes the presentation side from each built scene: the
    /// presentation bridge, UI documents (HUD, options) and cameras with their objects. A server build has no shaders,
    /// so UI Toolkit throws every frame, and nothing on a server is ever seen or heard. Works on the build's copy of
    /// the scene; the scene file does not change. Simulation (subscenes, ghosts) is untouched.
    /// </summary>
    internal sealed class ServerSceneStrip : IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null || EditorUserBuildSettings.standaloneBuildSubtarget != StandaloneBuildSubtarget.Server) return;

            var removed = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (!IsPresentation(root)) continue;
                Object.DestroyImmediate(root);
                removed++;
            }
            if (removed > 0) Debug.Log($"Server build: removed {removed} presentation object(s) from {scene.name}.");
        }

        private static bool IsPresentation(GameObject root) =>
            root.GetComponentInChildren<PresentationBridge>(true) != null ||
            root.GetComponentInChildren<PanelRenderer>(true) != null ||
            root.GetComponentInChildren<UIDocument>(true) != null ||
            root.GetComponentInChildren<UnityEngine.Camera>(true) != null;
    }
}
