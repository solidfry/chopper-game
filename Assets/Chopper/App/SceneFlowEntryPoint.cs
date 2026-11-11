using System;
using Chopper.Presentation.Bridge;
using Chopper.Presentation.Flow;
using Chopper.UI.Notifications;
using Chopper.UI.Transitions;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using VContainer.Unity;
using Object = UnityEngine.Object;

namespace Chopper.App
{
    /// <summary>
    /// Runs the game's <see cref="SceneFlow"/> (players' builds only): makes it with its transition overlay, hands it to
    /// each scene's <see cref="PresentationBridge"/> as the scene loads (music and volumes then live across scenes, and
    /// scene loads show the block transition), and ticks it every frame.
    /// </summary>
    public sealed class SceneFlowEntryPoint : IStartable, ITickable, IDisposable
    {
        /// <summary>Over every other panel (HUD 0, options 10, hangar).</summary>
        private const int OverlaySortingOrder = 1000;
        /// <summary>Toasts sit over the transition, so a notice posted during a load still reads.</summary>
        private const int NoticeSortingOrder = 1001;

        private readonly SceneTransitionSettings _settings;
        private readonly NotificationFeed _notices;
        private SceneFlow _flow;

        public SceneFlowEntryPoint(SceneTransitionSettings settings, NotificationFeed notices)
        {
            _settings = settings;
            _notices = notices;
        }

        public void Start()
        {
            _flow = new SceneFlow(_settings, _notices);
            CreateOverlay();
            CreateNotices();
            SceneManager.sceneLoaded += OnSceneLoaded;
            // The first scene loaded before this root scope existed.
            for (var i = 0; i < SceneManager.sceneCount; i++) Attach(SceneManager.GetSceneAt(i));
        }

        public void Tick() => _flow?.Tick(Time.unscaledDeltaTime);

        public void Dispose()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _flow?.Dispose();
            _flow = null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Attach(scene);

        private void Attach(Scene scene)
        {
            if (_flow == null || !scene.isLoaded) return;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var bridge in root.GetComponentsInChildren<PresentationBridge>(true))
                bridge.UseSceneFlow(_flow);
        }

        /// <summary>Own panel on the flow's persistent object. Without panel settings or layout: no overlay (scenes still cut).</summary>
        private void CreateOverlay()
        {
            if (_settings == null || _settings.PanelSettings == null || _settings.Layout == null)
            {
                Debug.LogWarning("Scene flow: no transition panel or layout (press Chopper → Setup, 1). Scene changes cut without the effect.");
                return;
            }

            var go = new GameObject("Scene Transition");
            go.SetActive(false);
            go.transform.SetParent(_flow.Root.transform, false);
            var panel = go.AddComponent<PanelRenderer>();
            panel.panelSettings = _settings.PanelSettings;
            panel.visualTreeAsset = _settings.Layout;
            panel.sortingOrder = OverlaySortingOrder;
            go.AddComponent<SceneTransitionView>().Bind(_flow.Model, _settings);
            go.SetActive(true);
        }

        /// <summary>The toast panel, also on the flow's persistent object. Without a layout: notices only go to the log.</summary>
        private void CreateNotices()
        {
            if (_settings == null || _settings.PanelSettings == null || _settings.NoticeLayout == null)
            {
                Debug.LogWarning("Scene flow: no notice layout (press Chopper → Setup, 1). Toast notices do not show.");
                return;
            }

            var go = new GameObject("Notifications");
            go.SetActive(false);
            go.transform.SetParent(_flow.Root.transform, false);
            var panel = go.AddComponent<PanelRenderer>();
            panel.panelSettings = _settings.PanelSettings;
            panel.visualTreeAsset = _settings.NoticeLayout;
            panel.sortingOrder = NoticeSortingOrder;
            go.AddComponent<NotificationsView>().Bind(_flow.Notices);
            go.SetActive(true);
        }
    }
}
