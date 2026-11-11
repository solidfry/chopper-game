using System;
using System.Collections.Generic;
using Chopper.Presentation.Menu;
using UnityEngine.UIElements;

namespace Chopper.UI.Home
{
    /// <summary>
    /// The home screen's mode select page (<c>page-modes</c> in Home.uxml, styles in Modes.uss), opened by PLAY: one
    /// tall card per <see cref="GameModeCatalog"/> mode, OFFLINE when it is not built yet. Hover or focus shows a mode
    /// in the strip under the cards; a click on a card (or DEPLOY) raises <see cref="Deploy"/>. <see cref="HomeView"/>
    /// owns it, plays the sounds and opens the hangar.
    /// </summary>
    internal sealed class ModesPage
    {
        private const string ShownClass = "mode-card--shown";
        private const string OfflineClass = "mode-card--offline";
        private const string SelectedClass = "mode-card--selected";
        private const string AccentPrefix = "mode--";

        private readonly HomeModel _model;
        private readonly VisualElement _list;
        private readonly VisualElement _detail;
        private readonly Label _queue;
        private readonly Label _detailName;
        private readonly Label _detailText;
        private readonly Label _state;
        private readonly Button _deploy;
        private readonly List<Button> _cards = new();
        private string _detailAccent;

        public ModesPage(VisualElement root, HomeModel model)
        {
            _model = model;
            _list = root.Q("modes-list");
            _detail = root.Q("modes-detail");
            _queue = root.Q<Label>("modes-queue");
            _detailName = root.Q<Label>("modes-detail-name");
            _detailText = root.Q<Label>("modes-detail-text");
            _state = root.Q<Label>("modes-state");
            _deploy = root.Q<Button>("modes-deploy");
            Back = root.Q<Button>("modes-back");
            if (_deploy != null) _deploy.clicked += OnDeploy;
            _model.ModeShownChanged += ShowShown;
        }

        /// <summary>The shown mode was picked (a card click, DEPLOY, or Submit on a card).</summary>
        public event Action Deploy;
        /// <summary>The pointer or focus moved to another card (the view plays the navigate sound).</summary>
        public event Action Moved;

        public Button Back { get; }

        public void Dispose()
        {
            if (_deploy != null) _deploy.clicked -= OnDeploy;
            _model.ModeShownChanged -= ShowShown;
            _cards.Clear();
        }

        /// <summary>(Re)builds the cards and shows the model's shown mode. Call when the page opens.</summary>
        public void Build()
        {
            if (_list == null) return;
            _list.Clear();
            _cards.Clear();
            var selected = _model.SelectedMode;
            for (var i = 0; i < _model.Modes.Count; i++)
            {
                var mode = _model.Modes[i];
                var index = i;
                var card = new Button { focusable = true };
                card.AddToClassList("mode-card");
                card.AddToClassList(AccentClass(mode.Accent));
                card.EnableInClassList(OfflineClass, !mode.Available);
                card.EnableInClassList(SelectedClass, mode == selected);

                var top = Row(card, "mode-card__top");
                Text(top, HomeModel.Serial(i), "mode-card__serial chopper-title");
                Text(top, StatusText(mode, selected), "mode-card__status chopper-title");
                Text(card, Upper(mode.DisplayName), "mode-card__name chopper-title");
                Text(card, Upper(mode.Tagline), "mode-card__tagline chopper-title");
                var rule = new VisualElement { pickingMode = PickingMode.Ignore };
                rule.AddToClassList("mode-card__rule");
                card.Add(rule);
                var rules = new VisualElement { pickingMode = PickingMode.Ignore };
                rules.AddToClassList("mode-card__rules");
                card.Add(rules);
                foreach (var line in Lines(mode.Rules)) Text(rules, "> " + line.ToUpperInvariant(), "mode-card__line chopper-title");
                Text(card, Upper(mode.Players), "mode-card__players chopper-title");

                card.RegisterCallback<PointerEnterEvent>(_ => Show(index));
                card.RegisterCallback<FocusInEvent>(_ => Show(index));
                card.clicked += () =>
                {
                    _model.ShowMode(index);
                    OnDeploy();
                };
                _list.Add(card);
                _cards.Add(card);
            }
            ShowShown();
        }

        /// <summary>Focus the shown card (page opened, or stepped with Q / E).</summary>
        public void FocusShown()
        {
            var shown = _model.ShownMode;
            if (shown >= 0 && shown < _cards.Count) _cards[shown].Focus();
            else _deploy?.Focus();
        }

        private void Show(int index)
        {
            if (index == _model.ShownMode) return;
            _model.ShowMode(index);
            Moved?.Invoke();
        }

        private void ShowShown()
        {
            var shown = _model.ShownMode;
            for (var i = 0; i < _cards.Count; i++) _cards[i].EnableInClassList(ShownClass, i == shown);
            var mode = _model.ShownModeEntry;
            var selected = _model.SelectedMode;
            if (_queue != null) _queue.text = selected != null ? "QUEUED // " + Upper(selected.DisplayName) : "";
            if (mode == null)
            {
                if (_detailName != null) _detailName.text = "NO MODES ON FILE";
                if (_detailText != null) _detailText.text = "";
                if (_state != null) _state.text = "";
                _deploy?.SetEnabled(false);
                return;
            }
            if (_detail != null)
            {
                if (_detailAccent != null) _detail.RemoveFromClassList(_detailAccent);
                _detailAccent = AccentClass(mode.Accent);
                _detail.AddToClassList(_detailAccent);
            }
            if (_detailName != null) _detailName.text = Upper(mode.DisplayName);
            if (_detailText != null) _detailText.text = mode.Description ?? "";
            if (_state != null) _state.text = mode.Available ? "LINK READY" : "OFFLINE // IN DEVELOPMENT";
            _deploy?.SetEnabled(mode.Available);
        }

        private void OnDeploy()
        {
            var mode = _model.ShownModeEntry;
            if (mode == null || !mode.Available) return;
            Deploy?.Invoke();
        }

        private static string StatusText(GameModeCatalog.ModeEntry mode, GameModeCatalog.ModeEntry selected) =>
            !mode.Available ? "OFFLINE" : mode == selected ? "QUEUED" : "ONLINE";

        private static string AccentClass(ModeAccent accent) => AccentPrefix + accent.ToString().ToLowerInvariant();

        private static string Upper(string text) => string.IsNullOrEmpty(text) ? "" : text.ToUpperInvariant();

        private static IEnumerable<string> Lines(string text)
        {
            if (string.IsNullOrEmpty(text)) yield break;
            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0) yield return trimmed;
            }
        }

        private static VisualElement Row(VisualElement parent, string className)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList(className);
            parent.Add(row);
            return row;
        }

        private static void Text(VisualElement parent, string text, string classNames)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            foreach (var name in classNames.Split(' ')) label.AddToClassList(name);
            parent.Add(label);
        }
    }
}
