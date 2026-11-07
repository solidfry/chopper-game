using System.Collections.Generic;
using Chopper.Presentation.Hud;
using UnityEngine.UIElements;

namespace Chopper.UI.Hud
{
    /// <summary>
    /// The scoreboard part of <c>ChopperHud.uxml</c>: one row per player from <see cref="MatchHudModel.Scoreboard"/>
    /// (place, name, chassis, kills, deaths), the own row in yellow. Rows are pooled and filled only while it shows.
    /// Shown while the scoreboard key is held and on the results (<see cref="MatchHudModel.ScoreboardVisible"/>).
    /// Made by <see cref="ChopperHudView"/> each time its tree is built.
    /// </summary>
    public sealed class ScoreboardView
    {
        private const string HiddenClass = "hud-scoreboard--hidden";
        private const string ResultsClass = "hud-match--results";
        private const string RowClass = "hud-scoreboard__row";
        private const string OddClass = "hud-scoreboard__row--odd";
        private const string LocalClass = "hud-scoreboard__row--local";
        private const string FirstClass = "hud-scoreboard__row--first";

        private readonly MatchHudModel _model;
        private readonly VisualElement _matchLayer;
        private readonly VisualElement _root;
        private readonly VisualElement _rowsParent;
        private readonly List<Row> _rows = new();
        private bool _dirty = true;

        private sealed class Row
        {
            public VisualElement Element;
            public Label Place;
            public Label Name;
            public Label Chassis;
            public Label Kills;
            public Label Deaths;
        }

        public ScoreboardView(VisualElement matchLayer, MatchHudModel model)
        {
            _model = model;
            _matchLayer = matchLayer;
            _root = matchLayer?.Q("scoreboard");
            _rowsParent = _root?.Q("scoreboard-rows");
            if (_root == null || _rowsParent == null) return;

            _root.dataSource = model;
            _root.Q<Label>("scoreboard-title")?.SetBinding("text", ToTarget(nameof(MatchHudModel.ScoreboardTitle)));
            var timer = ToTarget(nameof(MatchHudModel.TimerText));
            timer.sourceToUiConverters.AddConverter((ref string value) => TabularText.Wrap(value));
            _root.Q<Label>("scoreboard-timer")?.SetBinding("text", timer);
            _model.ScoreboardChanged += OnScoreboardChanged;
            _model.propertyChanged += OnModelChanged;
            RefreshVisible();
        }

        public void Dispose()
        {
            if (_root == null || _rowsParent == null) return;
            _model.ScoreboardChanged -= OnScoreboardChanged;
            _model.propertyChanged -= OnModelChanged;
        }

        private void OnModelChanged(object sender, BindablePropertyChangedEventArgs args)
        {
            if (args.propertyName == nameof(MatchHudModel.ScoreboardVisible) ||
                args.propertyName == nameof(MatchHudModel.BannerVisible))
                RefreshVisible();
        }

        private void OnScoreboardChanged()
        {
            _dirty = true;
            if (_model.ScoreboardVisible) Fill();
        }

        private void RefreshVisible()
        {
            var visible = _model.ScoreboardVisible;
            _root.EnableInClassList(HiddenClass, !visible);
            _matchLayer.EnableInClassList(ResultsClass, _model.BannerVisible);
            if (visible && _dirty) Fill();
        }

        private void Fill()
        {
            _dirty = false;
            var players = _model.Scoreboard;
            while (_rows.Count < players.Count) _rows.Add(MakeRow());
            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                var shown = i < players.Count;
                row.Element.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
                if (!shown) continue;

                var player = players[i];
                row.Place.text = player.Place.ToString();
                row.Name.text = player.Name;
                row.Chassis.text = player.Chassis;
                row.Kills.text = TabularText.Wrap(player.Kills.ToString());
                row.Deaths.text = TabularText.Wrap(player.Deaths.ToString());
                row.Element.EnableInClassList(OddClass, i % 2 == 1);
                row.Element.EnableInClassList(LocalClass, player.IsLocal);
                row.Element.EnableInClassList(FirstClass, player.Place == 1 && player.Kills > 0);
            }
        }

        private Row MakeRow()
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.AddToClassList(RowClass);
            var row = new Row
            {
                Element = element,
                Place = Cell(element, "hud-scoreboard__place"),
                Name = Cell(element, "hud-scoreboard__name"),
                Chassis = Cell(element, "hud-scoreboard__chassis"),
                Kills = Cell(element, "hud-scoreboard__number"),
                Deaths = Cell(element, "hud-scoreboard__number"),
            };
            _rowsParent.Add(element);
            return row;
        }

        private static Label Cell(VisualElement row, string className)
        {
            var label = new Label { pickingMode = PickingMode.Ignore };
            label.AddToClassList(className);
            row.Add(label);
            return label;
        }

        private static DataBinding ToTarget(string property) => new()
        {
            dataSourcePath = new Unity.Properties.PropertyPath(property),
            bindingMode = BindingMode.ToTarget,
        };
    }
}
