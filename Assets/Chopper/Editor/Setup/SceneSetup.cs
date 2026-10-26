using Chopper.Presentation.Bridge;
using Chopper.Presentation.Camera;
using Chopper.Presentation.Config;
using Chopper.UI.Audio;
using Chopper.UI.Hud;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// Sets up the open scene: one Presentation object (bridge to ECS) and the HUD document wired to it.
    /// Reuses what is already there.
    /// </summary>
    internal static class SceneSetup
    {
        public static void Apply(GameConfig config)
        {
            var bridge = Object.FindFirstObjectByType<PresentationBridge>();
            if (bridge == null)
            {
                var go = new GameObject("Presentation");
                Undo.RegisterCreatedObjectUndo(go, "Add Presentation");
                bridge = Undo.AddComponent<PresentationBridge>(go);
            }
            Undo.RecordObject(bridge, "Configure Presentation");
            bridge.Configure(config, Object.FindFirstObjectByType<ChopperCameraRig>());
            EditorUtility.SetDirty(bridge);

            var hud = Object.FindFirstObjectByType<ChopperHudView>();
            var hudObject = hud != null ? hud.gameObject : CreateHudObject();
            // Old HUD objects held a component that no longer exists.
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(hudObject);

            var document = hudObject.GetComponent<UIDocument>();
            if (document == null) document = Undo.AddComponent<UIDocument>(hudObject);
            Undo.RecordObject(document, "Configure HUD");
            document.panelSettings = config != null ? config.PanelSettings : null;
            document.visualTreeAsset = config != null ? config.HudLayout : null;

            // No "??" here: in the editor GetComponent returns a fake-null object, not real null.
            hud = hudObject.GetComponent<ChopperHudView>();
            if (hud == null) hud = Undo.AddComponent<ChopperHudView>(hudObject);
            Undo.RecordObject(hud, "Configure HUD");
            hud.SetBridge(bridge);

            var sounds = hudObject.GetComponent<UISoundBinder>();
            if (sounds == null) sounds = Undo.AddComponent<UISoundBinder>(hudObject);
            Undo.RecordObject(sounds, "Configure HUD");
            sounds.SetBridge(bridge);

            EditorUtility.SetDirty(document);
            EditorUtility.SetDirty(hud);
            EditorUtility.SetDirty(sounds);
            EditorSceneManager.MarkSceneDirty(bridge.gameObject.scene);
            Selection.activeGameObject = bridge.gameObject;
        }

        private static GameObject CreateHudObject()
        {
            var go = new GameObject("HUD");
            Undo.RegisterCreatedObjectUndo(go, "Add HUD");
            return go;
        }
    }
}
