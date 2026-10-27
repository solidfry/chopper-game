using Chopper.Presentation.Bridge;
using Chopper.Presentation.Camera;
using Chopper.Presentation.Config;
using Chopper.UI.Audio;
using Chopper.UI.Hud;
using Chopper.UI.Options;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// Sets up the open scene: one Presentation object (bridge to ECS), the chase camera, the HUD document and the
    /// options overlay, wired to it.
    /// Reuses what is already there.
    /// </summary>
    internal static class SceneSetup
    {
        public static void Apply(GameConfig config)
        {
            var bridge = Object.FindAnyObjectByType<PresentationBridge>();
            if (bridge == null)
            {
                var go = new GameObject("Presentation");
                Undo.RegisterCreatedObjectUndo(go, "Add Presentation");
                bridge = Undo.AddComponent<PresentationBridge>(go);
            }
            Undo.RecordObject(bridge, "Configure Presentation");
            bridge.Configure(config, EnsureCameraRig());
            EditorUtility.SetDirty(bridge);

            var hud = Object.FindAnyObjectByType<ChopperHudView>();
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
            EnsureOptions(config, bridge);
            EditorSceneManager.MarkSceneDirty(bridge.gameObject.scene);
            Selection.activeGameObject = bridge.gameObject;
        }

        /// <summary>The chase camera: on the existing main camera, or a new one if the scene has none.</summary>
        private static ChopperCameraRig EnsureCameraRig()
        {
            var rig = Object.FindAnyObjectByType<ChopperCameraRig>(FindObjectsInactive.Include);
            if (rig != null) return rig;

            var camera = UnityEngine.Camera.main;
            if (camera == null) camera = Object.FindAnyObjectByType<UnityEngine.Camera>(FindObjectsInactive.Include);
            if (camera == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                Undo.RegisterCreatedObjectUndo(go, "Add Main Camera");
                camera = go.AddComponent<UnityEngine.Camera>();
                go.AddComponent<AudioListener>();
            }
            return Undo.AddComponent<ChopperCameraRig>(camera.gameObject);
        }

        /// <summary>Options overlay: its own document on the shared panel settings (not the HUD's render texture), drawn on top.</summary>
        private static void EnsureOptions(GameConfig config, PresentationBridge bridge)
        {
            var view = Object.FindAnyObjectByType<OptionsMenuView>(FindObjectsInactive.Include);
            GameObject go;
            if (view != null)
            {
                go = view.gameObject;
            }
            else
            {
                go = new GameObject("Options");
                Undo.RegisterCreatedObjectUndo(go, "Add Options");
            }

            var document = go.GetComponent<UIDocument>();
            if (document == null) document = Undo.AddComponent<UIDocument>(go);
            Undo.RecordObject(document, "Configure Options");
            document.panelSettings = config != null ? config.PanelSettings : null;
            document.visualTreeAsset = config != null ? config.OptionsLayout : null;
            document.sortingOrder = 100;

            view = go.GetComponent<OptionsMenuView>();
            if (view == null) view = Undo.AddComponent<OptionsMenuView>(go);
            Undo.RecordObject(view, "Configure Options");
            view.SetBridge(bridge);

            var sounds = go.GetComponent<UISoundBinder>();
            if (sounds == null) sounds = Undo.AddComponent<UISoundBinder>(go);
            Undo.RecordObject(sounds, "Configure Options");
            sounds.SetBridge(bridge);

            EditorUtility.SetDirty(document);
            EditorUtility.SetDirty(view);
            EditorUtility.SetDirty(sounds);
        }

        private static GameObject CreateHudObject()
        {
            var go = new GameObject("HUD");
            Undo.RegisterCreatedObjectUndo(go, "Add HUD");
            return go;
        }
    }
}
