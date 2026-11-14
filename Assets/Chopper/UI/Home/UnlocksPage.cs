using System;
using System.Collections.Generic;
using Chopper.Presentation.Profile;
using Chopper.UI.Cockpit;
using UnityEngine.UIElements;

namespace Chopper.UI.Home
{
    /// <summary>
    /// The UNLOCKS page (<c>page-unlocks</c> in Home.uxml, styled in Unlocks.uss): three tabs over
    /// <see cref="UnlocksModel"/>. ACCOUNT = a level ladder with what each level unlocks; CHASSIS = a class picker, its
    /// mastery bar and its part chains; PILOTS = each pilot's mastery (rewards come later). Lists are made in code.
    /// </summary>
    internal sealed class UnlocksPage
    {
        private enum Tab { Account, Chassis, Pilots }

        private readonly Label _level;
        private readonly ScrollView _list;
        private readonly Dictionary<Tab, Button> _tabs = new();
        private readonly VisualElement _classes;
        private Tab _tab = Tab.Account;
        /// <summary>The row to scroll to after layout (the NEXT level); null = the top.</summary>
        private VisualElement _scrollTarget;
        private int _class;
        private UnlocksModel _model;

        public UnlocksPage(VisualElement root)
        {
            Back = root.Q<Button>("unlocks-back");
            _level = root.Q<Label>("ul-level");
            _list = root.Q<ScrollView>("ul-list");
            _classes = root.Q("ul-classes");
            AddTab(root, "ul-tab-account", Tab.Account);
            AddTab(root, "ul-tab-chassis", Tab.Chassis);
            AddTab(root, "ul-tab-pilots", Tab.Pilots);
        }

        public Button Back { get; }

        public void Show(UnlocksModel model)
        {
            _model = model;
            BuildClasses();
            Rebuild();
        }

        private void BuildClasses()
        {
            if (_classes == null) return;
            _classes.Clear();
            for (var i = 0; i < _model.ClassCount; i++)
            {
                var pick = i;
                var button = new Button(() => { _class = pick; Rebuild(); }) { text = _model.ClassName(i), userData = i };
                button.AddToClassList("ul-class");
                button.AddToClassList("chopper-title");
                _classes.Add(button);
            }
        }

        private void AddTab(VisualElement root, string name, Tab tab)
        {
            var button = root.Q<Button>(name);
            if (button == null) return;
            button.clicked += () => { _tab = tab; Rebuild(); };
            _tabs[tab] = button;
        }

        private void Rebuild()
        {
            if (_model == null || _list == null) return;
            if (_level != null) _level.text = $"LVL {_model.Level}";
            foreach (var pair in _tabs) pair.Value.EnableInClassList("ul-tab--active", pair.Key == _tab);
            if (_classes != null)
            {
                _classes.style.display = _tab == Tab.Chassis ? DisplayStyle.Flex : DisplayStyle.None;
                foreach (var child in _classes.Children())
                    child.EnableInClassList("ul-class--active", child.userData is int c && c == _class);
            }

            _list.Clear();
            _scrollTarget = null;
            switch (_tab)
            {
                case Tab.Account: FillAccount(); break;
                case Tab.Chassis: FillChassis(); break;
                default: FillPilots(); break;
            }
            // After layout: show the next level (Account), else the top. Before layout the sizes are not known.
            _list.schedule.Execute(() =>
            {
                if (_scrollTarget != null) _list.ScrollTo(_scrollTarget);
                else _list.scrollOffset = UnityEngine.Vector2.zero;
            }).ExecuteLater(1);
        }

        // ---- ACCOUNT: one row per level that unlocks something ----

        private void FillAccount()
        {
            if (_model.AccountTrack is { } account) _list.Add(Track(account, "ACCOUNT LEVEL"));
            var levels = _model.AccountLevels();
            if (levels.Count == 0)
            {
                _list.Add(Text("Nothing to unlock.", "ul-empty"));
                return;
            }
            foreach (var level in levels)
            {
                var row = Element("ul-level");
                row.AddToClassList(StateClass("ul-level", level.State));
                var badge = Element("ul-level__badge");
                badge.Add(Text(level.Level <= 1 ? "START" : "LVL", "ul-level__caption"));
                if (level.Level > 1) badge.Add(Text(level.Level.ToString(), "ul-level__number", "chopper-title"));
                row.Add(badge);
                var items = Element("ul-level__items");
                foreach (var item in level.Items) items.Add(Chip(item));
                row.Add(items);
                if (level.State == UnlockState.Next)
                {
                    row.Add(Text("NEXT", "ul-level__next", "chopper-title"));
                    _scrollTarget = row;
                }
                _list.Add(row);
            }
        }

        // ---- CHASSIS: mastery bar and part chains for the picked class ----

        private void FillChassis()
        {
            if (_model.ClassTrack(_class) is { } track) _list.Add(Track(track, $"{_model.ClassName(_class)} MASTERY"));
            var chains = _model.ChainsAt(_class);
            if (chains.Count == 0)
            {
                _list.Add(Text("No part chains for this class yet.", "ul-empty"));
                return;
            }
            foreach (var chain in chains)
            {
                var row = Element("ul-chain");
                row.Add(Text(chain.Kind, "ul-chain__kind", "chopper-title"));
                var tiers = Element("ul-chain__tiers");
                for (var i = 0; i < chain.Tiers.Count; i++)
                {
                    if (i > 0) tiers.Add(Text(">", "ul-chain__arrow"));
                    tiers.Add(Chip(chain.Tiers[i], showKind: false));
                }
                row.Add(tiers);
                _list.Add(row);
            }
        }

        // ---- PILOTS: mastery per pilot ----

        private void FillPilots()
        {
            _list.Add(Text("Pilot mastery rewards (portraits, titles, voices) come later. Your levels:", "ul-note"));
            foreach (var track in _model.PilotTracks()) _list.Add(Track(track, track.Label));
        }

        // ---- Parts ----

        private static VisualElement Track(TrackProgress track, string title)
        {
            var row = Element("ul-track");
            var head = Element("ul-track__head");
            head.Add(Text(title, "ul-track__title", "chopper-title"));
            head.Add(Text(track.LevelText, "ul-track__level", "chopper-title"));
            row.Add(head);
            var bar = new SegmentBar { segments = 20, gap = 2f, value = track.Fill, pickingMode = PickingMode.Ignore };
            bar.AddToClassList("ul-track__bar");
            row.Add(bar);
            row.Add(Text(track.NeedText, "ul-track__need"));
            return row;
        }

        private static VisualElement Chip(UnlockItem item, bool showKind = true)
        {
            var chip = Element("ul-chip");
            chip.AddToClassList(StateClass("ul-chip", item.State));
            if (showKind) chip.Add(Text(item.Kind, "ul-chip__kind"));
            chip.Add(Text(item.Name, "ul-chip__name", "chopper-title"));
            if (item.Reason.Length > 0) chip.Add(Text(item.Reason, "ul-chip__reason"));
            return chip;
        }

        private static string StateClass(string block, UnlockState state) => state switch
        {
            UnlockState.Open => block + "--open",
            UnlockState.Next => block + "--next",
            _ => block + "--locked",
        };

        private static VisualElement Element(string className)
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.AddToClassList(className);
            return element;
        }

        private static Label Text(string text, params string[] classes)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            foreach (var c in classes) label.AddToClassList(c);
            return label;
        }
    }
}
