using System.Collections.Generic;
using System.IO;
using Chopper.App;
using Chopper.Presentation.Audio;
using Chopper.Presentation.Flow;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UIElements;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// Scene changes (step 1): <c>Data/UI/SceneTransitionSettings.asset</c> with its own panel settings (a copy of the
    /// game's, sort order 1000, so the transition draws over every panel), the overlay layout and two sounds; linked to
    /// the root scope prefab (the scene flow lives in the root scope). Creates only what is missing.
    /// </summary>
    internal static class TransitionAssets
    {
        private const string SettingsPath = "Assets/Chopper/Data/UI/SceneTransitionSettings.asset";
        private const string PanelPath = "Assets/Chopper/UI/Settings/TransitionPanelSettings.asset";
        private const string LayoutPath = "Assets/Chopper/UI/Transitions/SceneTransition.uxml";
        private const string NoticeLayoutPath = "Assets/Chopper/UI/Notifications/Notifications.uxml";
        private const string RootPrefabPath = "Assets/Chopper/App/RootLifetimeScope.prefab";

        public static SceneTransitionSettings Ensure(PanelSettings shared, AudioMixer mixer)
        {
            var settings = AssetDatabase.LoadAssetAtPath<SceneTransitionSettings>(SettingsPath);
            if (settings == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                settings = ScriptableObject.CreateInstance<SceneTransitionSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }

            var ui = mixer != null ? AudioAssets.Group(mixer, "UI") : null;
            settings.Initialise(
                settings.PanelSettings != null ? settings.PanelSettings : EnsurePanel(shared),
                settings.Layout != null ? settings.Layout : AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(LayoutPath),
                // Cover: the computer whirrs (data spinning up). Reveal: a short laser charge, pitched up.
                settings.CoverSound != null ? settings.CoverSound
                    : AudioAssets.Cue("TransitionCover", AudioAssets.ClipsIn("Assets/Sound/UI/ComputerWirrSounds"), ui, 0.55f, blend: 0f, voices: 1),
                settings.RevealSound != null ? settings.RevealSound
                    : AudioAssets.Cue("TransitionReveal", AudioAssets.Clips(AudioAssets.SciFiGuns + "/Weapon 05 Load (Laser).wav"), ui, 0.35f,
                        blend: 0f, voices: 1, pitch: 1.4f));
            // Toasts: a terminal line print; errors a low switch clunk.
            settings.SetNotices(
                settings.NoticeLayout != null ? settings.NoticeLayout : AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(NoticeLayoutPath),
                settings.NoticeSound != null ? settings.NoticeSound
                    : AudioAssets.Cue("Notice", AudioAssets.Clips("Assets/Sound/UI/Terminal/Term_Line_1.wav"), ui, 0.4f, blend: 0f, voices: 2),
                settings.NoticeErrorSound != null ? settings.NoticeErrorSound
                    : AudioAssets.Cue("NoticeError", AudioAssets.Clips("Assets/Sound/UI/Switch/Switch (4).wav"), ui, 0.5f, blend: 0f, voices: 1,
                        pitch: 0.7f));
            EditorUtility.SetDirty(settings);

            var root = AssetDatabase.LoadAssetAtPath<RootLifetimeScope>(RootPrefabPath);
            if (root != null && root.SceneTransitions == null)
            {
                root.SetSceneTransitions(settings);
                EditorUtility.SetDirty(root);
                PrefabUtility.SavePrefabAsset(root.gameObject);
            }
            AssetDatabase.SaveAssets();
            return settings;
        }

        public static IEnumerable<string> Problems()
        {
            var root = AssetDatabase.LoadAssetAtPath<RootLifetimeScope>(RootPrefabPath);
            if (root == null) yield break;
            if (root.SceneTransitions == null)
            {
                yield return "The root scope prefab has no scene transition settings: scene changes cut, music restarts. Press '1. Create / repair config'.";
                yield break;
            }
            var messages = new List<string>();
            root.SceneTransitions.CollectProblems(messages);
            foreach (var message in messages) yield return message;
        }

        /// <summary>A copy of the game's panel settings (same theme and scaling), drawn over every other panel.</summary>
        private static PanelSettings EnsurePanel(PanelSettings shared)
        {
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (panel == null)
            {
                panel = shared != null ? Object.Instantiate(shared) : ScriptableObject.CreateInstance<PanelSettings>();
                Directory.CreateDirectory(Path.GetDirectoryName(PanelPath));
                AssetDatabase.CreateAsset(panel, PanelPath);
            }
            panel.targetTexture = null;
            panel.sortingOrder = 1000;
            panel.clearColor = false;
            EditorUtility.SetDirty(panel);
            return panel;
        }
    }
}
