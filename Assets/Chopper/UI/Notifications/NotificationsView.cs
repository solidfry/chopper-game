using System.Collections.Generic;
using Chopper.Presentation.Flow;
using UnityEngine;
using UnityEngine.UIElements;

namespace Chopper.UI.Notifications
{
    /// <summary>
    /// Draws the game's toast notices (<see cref="NotificationFeed"/>) top right, over every scene. Lives on the scene
    /// flow's persistent object with its own <see cref="PanelRenderer"/> (made by the root scope's entry point), next to
    /// the transition overlay. A notice slides in, its bottom bar runs down, and it slides out; a click removes it.
    /// Only the notices take clicks: the rest of the panel lets them through.
    /// </summary>
    [RequireComponent(typeof(PanelRenderer))]
    public sealed class NotificationsView : MonoBehaviour
    {
        private const string EnterClass = "notice--enter";
        private const string LeaveClass = "notice--leave";
        /// <summary>As long as the leave transition in Notifications.uss.</summary>
        private const long LeaveMilliseconds = 220;

        private NotificationFeed _feed;
        private PanelRenderer _renderer;
        private VisualElement _stack;
        private readonly Dictionary<int, Entry> _entries = new();

        private sealed class Entry
        {
            public ShownNotice Notice;
            public VisualElement Root;
            public VisualElement Timer;
            public Label Count;
        }

        /// <summary>Call before the object is active.</summary>
        public void Bind(NotificationFeed feed) => _feed = feed;

        private void OnEnable()
        {
            _renderer = GetComponent<PanelRenderer>();
            _renderer.RegisterUIReloadCallback(OnUIReload);
            if (_feed == null) return;
            _feed.Shown += OnShown;
            _feed.Hidden += OnHidden;
            _feed.Repeated += OnRepeated;
        }

        private void OnDisable()
        {
            if (_renderer != null) _renderer.UnregisterUIReloadCallback(OnUIReload);
            if (_feed != null)
            {
                _feed.Shown -= OnShown;
                _feed.Hidden -= OnHidden;
                _feed.Repeated -= OnRepeated;
            }
            _entries.Clear();
            _stack = null;
        }

        private void OnUIReload(PanelRenderer panelRenderer, VisualElement rootElement, int version)
        {
            _entries.Clear();
            _stack = rootElement?.Q("notices");
            if (_stack == null || _feed == null) return;
            _stack.Clear();
            // A rebuilt panel shows what is up now, without the slide in.
            foreach (var notice in _feed.Visible) Add(notice, animate: false);
        }

        private void LateUpdate()
        {
            foreach (var entry in _entries.Values)
                entry.Timer.style.width = Length.Percent((1f - entry.Notice.Progress) * 100f);
        }

        private void OnShown(ShownNotice notice) => Add(notice, animate: true);

        private void OnRepeated(ShownNotice notice)
        {
            if (!_entries.TryGetValue(notice.Id, out var entry)) return;
            entry.Count.text = "x" + notice.Count;
            entry.Count.style.display = DisplayStyle.Flex;
        }

        private void OnHidden(ShownNotice notice)
        {
            if (!_entries.Remove(notice.Id, out var entry)) return;
            var element = entry.Root;
            element.AddToClassList(LeaveClass);
            element.pickingMode = PickingMode.Ignore;
            element.schedule.Execute(() => element.RemoveFromHierarchy()).StartingIn(LeaveMilliseconds);
        }

        private void Add(ShownNotice notice, bool animate)
        {
            if (_stack == null || _entries.ContainsKey(notice.Id)) return;
            var root = new VisualElement();
            root.AddToClassList("notice");
            root.AddToClassList("notice--" + notice.Kind.ToString().ToLowerInvariant());

            var head = new VisualElement { pickingMode = PickingMode.Ignore };
            head.AddToClassList("notice__head");
            head.Add(Text(Mark(notice.Kind), "notice__mark chopper-title"));
            head.Add(Text(notice.Title, "notice__title chopper-title"));
            var count = Text("", "notice__count chopper-title");
            count.style.display = DisplayStyle.None;
            head.Add(count);
            root.Add(head);
            if (!string.IsNullOrEmpty(notice.Text)) root.Add(Text(notice.Text, "notice__text chopper-body"));

            var track = new VisualElement { pickingMode = PickingMode.Ignore };
            track.AddToClassList("notice__track");
            var timer = new VisualElement { pickingMode = PickingMode.Ignore };
            timer.AddToClassList("notice__timer");
            track.Add(timer);
            root.Add(track);

            var id = notice.Id;
            root.RegisterCallback<ClickEvent>(_ => _feed?.Dismiss(id));
            _stack.Add(root);
            _entries[id] = new Entry { Notice = notice, Root = root, Timer = timer, Count = count };
            if (notice.Count > 1) OnRepeated(notice);

            if (!animate) return;
            // Start off to the side, then drop the class on the next frame so the USS transition slides it in.
            root.AddToClassList(EnterClass);
            root.schedule.Execute(() => root.RemoveFromClassList(EnterClass)).StartingIn(16);
        }

        private static string Mark(NoticeKind kind) => kind switch
        {
            NoticeKind.Success => "[+]",
            NoticeKind.Warning => "[!]",
            NoticeKind.Error => "[X]",
            NoticeKind.Reward => "[*]",
            _ => "[>]",
        };

        private static Label Text(string text, string classNames)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            foreach (var name in classNames.Split(' ')) label.AddToClassList(name);
            return label;
        }
    }
}
