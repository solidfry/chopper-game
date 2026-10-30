using Chopper.Presentation.Bridge;
using Chopper.Presentation.Camera;
using Chopper.Presentation.Config;
using Chopper.UI.Audio;
using Chopper.UI.Cockpit;
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

            var panel = EnsurePanelRenderer(hudObject, config != null ? config.PanelSettings : null, config != null ? config.HudLayout : null,
                sortingOrder: 0);

            // No "??" here: in the editor GetComponent returns a fake-null object, not real null.
            hud = hudObject.GetComponent<ChopperHudView>();
            if (hud == null) hud = Undo.AddComponent<ChopperHudView>(hudObject);
            Undo.RecordObject(hud, "Configure HUD");
            hud.SetBridge(bridge);

            var sounds = hudObject.GetComponent<UISoundBinder>();
            if (sounds == null) sounds = Undo.AddComponent<UISoundBinder>(hudObject);
            Undo.RecordObject(sounds, "Configure HUD");
            sounds.SetBridge(bridge);

            EditorUtility.SetDirty(panel);
            EditorUtility.SetDirty(hud);
            EditorUtility.SetDirty(sounds);
            EnsureOptions(config, bridge);
            EnsureCockpitScreens(config, bridge);
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

        /// <summary>Options overlay: its own panel renderer on the shared panel settings (not the HUD's render texture), drawn on top.</summary>
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

            var panel = EnsurePanelRenderer(go, config != null ? config.PanelSettings : null, config != null ? config.OptionsLayout : null,
                sortingOrder: 100);

            view = go.GetComponent<OptionsMenuView>();
            if (view == null) view = Undo.AddComponent<OptionsMenuView>(go);
            Undo.RecordObject(view, "Configure Options");
            view.SetBridge(bridge);

            var sounds = go.GetComponent<UISoundBinder>();
            if (sounds == null) sounds = Undo.AddComponent<UISoundBinder>(go);
            Undo.RecordObject(sounds, "Configure Options");
            sounds.SetBridge(bridge);

            EditorUtility.SetDirty(panel);
            EditorUtility.SetDirty(view);
            EditorUtility.SetDirty(sounds);
        }

        /// <summary>Live cockpit screens: a panel renderer that draws into the screens' render texture (its own panel settings).</summary>
        private static void EnsureCockpitScreens(GameConfig config, PresentationBridge bridge)
        {
            var view = Object.FindAnyObjectByType<CockpitScreensView>(FindObjectsInactive.Include);
            GameObject go;
            if (view != null)
            {
                go = view.gameObject;
            }
            else
            {
                go = new GameObject("Cockpit Screens");
                Undo.RegisterCreatedObjectUndo(go, "Add Cockpit Screens");
            }

            var panel = EnsurePanelRenderer(go, config != null ? config.CockpitScreensPanel : null,
                config != null ? config.CockpitScreensLayout : null, sortingOrder: 0);

            view = go.GetComponent<CockpitScreensView>();
            if (view == null) view = Undo.AddComponent<CockpitScreensView>(go);
            Undo.RecordObject(view, "Configure Cockpit Screens");
            view.SetBridge(bridge);

            EditorUtility.SetDirty(panel);
            EditorUtility.SetDirty(view);
        }

        /// <summary>
        /// The object's <see cref="PanelRenderer"/> (Unity 6.6 UI component), set to the layout. A <see cref="UIDocument"/>
        /// left from before is removed: both would build the same layout, so the UI would show twice.
        /// </summary>
        private static PanelRenderer EnsurePanelRenderer(GameObject go, PanelSettings settings, VisualTreeAsset layout, int sortingOrder)
        {
            var panel = go.GetComponent<PanelRenderer>();
            if (panel == null) panel = Undo.AddComponent<PanelRenderer>(go);
            Undo.RecordObject(panel, "Configure panel renderer");
            panel.panelSettings = settings;
            panel.visualTreeAsset = layout;
            panel.sortingOrder = sortingOrder;

            var oldDocument = go.GetComponent<UIDocument>();
            if (oldDocument != null) Undo.DestroyObjectImmediate(oldDocument);
            return panel;
        }

        private static GameObject CreateHudObject()
        {
            var go = new GameObject("HUD");
            Undo.RegisterCreatedObjectUndo(go, "Add HUD");
            return go;
        }
    }
}
