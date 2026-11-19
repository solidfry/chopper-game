using System.Collections.Generic;
using Chopper.Presentation.Hud;
using UnityEngine.UIElements;

namespace Chopper.UI.Hud
{
    /// <summary>
    /// The scoreboard part of <c>ChopperHud.uxml</c>: a full-screen board with one column per team (one column in
    /// Free For All). Each column has a header (team name and team score, in the team colour), the column names and
    /// one row per player from <see cref="MatchHudModel.Scoreboard"/> (place, name, chassis, kills, deaths); the own
    /// row has a pink bar. Columns and rows are pooled and filled only while it shows.
    /// Shown while the scoreboard key is held and on the results (<see cref="MatchHudModel.ScoreboardVisible"/>).
    /// Made by <see cref="ChopperHudView"/> each time its tree is built.
    /// </summary>
    public sealed class ScoreboardView
    {
        private const string HiddenClass = "hud-scoreboard--hidden";
        private const string ResultsClass = "hud-match--results";
        private const string OverHudClass = "hud--scoreboard";
        private const string ColumnClass = "hud-scoreboard__column";
        private const string TeamClassPrefix = "hud-scoreboard__column--team";
        private const string OwnTeamClass = "hud-scoreboard__column--own";
        private const string RowClass = "hud-scoreboard__row";
        private const string OddClass = "hud-scoreboard__row--odd";
        private const string LocalClass = "hud-scoreboard__row--local";
        private const string FirstClass = "hud-scoreboard__row--first";
        private const int TeamColours = 8;

        private readonly MatchHudModel _model;
        private readonly VisualElement _matchLayer;
        private readonly VisualElement _root;
        private readonly VisualElement _columnsParent;
        private readonly List<Column> _columns = new();
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

        private sealed class Column
        {
            public VisualElement Element;
            public Label Team;
            public Label Score;
            public VisualElement Rows;
            public readonly List<Row> RowPool = new();
            public int Used;
            public int TeamClass = -1;
        }

        public ScoreboardView(VisualElement matchLayer, MatchHudModel model)
        {
            _model = model;
            _matchLayer = matchLayer;
            _root = matchLayer?.Q("scoreboard");
            _columnsParent = _root?.Q("scoreboard-rows");
            if (_root == null || _columnsParent == null) return;

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
            if (_root == null || _columnsParent == null) return;
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
            _matchLayer.parent?.EnableInClassList(OverHudClass, visible);
            if (visible && _dirty) Fill();
        }

        private void Fill()
        {
            _dirty = false;
            var players = _model.Scoreboard;
            var teams = _model.IsTeamMode;
            var ownTeam = _model.LocalTeam;
            foreach (var column in _columns) column.Used = 0;

            // Rows come grouped by team (team 1 first): a new column starts where the team changes.
            var columnCount = 0;
            Column current = null;
            byte currentTeam = 0;
            for (var i = 0; i < players.Count; i++)
            {
                var player = players[i];
                var team = teams ? player.Team : (byte)0;
                if (current == null || team != currentTeam)
                {
                    current = TakeColumn(columnCount++);
                    currentTeam = team;
                    SetHeader(current, teams, team, ownTeam);
                }

                var row = TakeRow(current);
                row.Place.text = player.Place.ToString();
                row.Name.text = player.Name;
                row.Chassis.text = player.Chassis;
                row.Kills.text = TabularText.Wrap(player.Kills.ToString());
                row.Deaths.text = TabularText.Wrap(player.Deaths.ToString());
                row.Element.EnableInClassList(OddClass, (current.Used - 1) % 2 == 1);
                row.Element.EnableInClassList(LocalClass, player.IsLocal);
                row.Element.EnableInClassList(FirstClass, player.Place == 1 && player.Kills > 0);
            }

            for (var c = 0; c < _columns.Count; c++)
            {
                var column = _columns[c];
                column.Element.style.display = c < columnCount ? DisplayStyle.Flex : DisplayStyle.None;
                for (var r = 0; r < column.RowPool.Count; r++)
                    column.RowPool[r].Element.style.display = r < column.Used ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void SetHeader(Column column, bool teams, byte team, byte ownTeam)
        {
            column.Team.text = teams ? MatchHudModel.TeamName(team) : "PILOTS";
            column.Score.text = teams ? TabularText.Wrap(_model.TeamScore(team).ToString()) : "";
            column.Score.style.display = teams ? DisplayStyle.Flex : DisplayStyle.None;
            var colour = teams ? (team - 1) % TeamColours + 1 : 0;
            if (column.TeamClass != colour)
            {
                if (column.TeamClass >= 0) column.Element.RemoveFromClassList(TeamClassPrefix + column.TeamClass);
                column.Element.AddToClassList(TeamClassPrefix + colour);
                column.TeamClass = colour;
            }
            column.Element.EnableInClassList(OwnTeamClass, teams && team == ownTeam);
        }

        private Column TakeColumn(int index)
        {
            if (index < _columns.Count) return _columns[index];
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.AddToClassList(ColumnClass);

            var header = new VisualElement { pickingMode = PickingMode.Ignore };
            header.AddToClassList("hud-scoreboard__team");
            element.Add(header);
            var column = new Column
            {
                Element = element,
                Team = Cell(header, "hud-scoreboard__team-name"),
                Score = Cell(header, "hud-scoreboard__team-score"),
            };
            column.Team.AddToClassList("chopper-title");

            var names = new VisualElement { pickingMode = PickingMode.Ignore };
            names.AddToClassList(RowClass);
            names.AddToClassList("hud-scoreboard__row--columns");
            Cell(names, "hud-scoreboard__place").text = "#";
            Cell(names, "hud-scoreboard__name").text = "PILOT";
            Cell(names, "hud-scoreboard__chassis").text = "CHOPPER";
            Cell(names, "hud-scoreboard__number").text = "K";
            Cell(names, "hud-scoreboard__number").text = "D";
            element.Add(names);

            column.Rows = new VisualElement { pickingMode = PickingMode.Ignore };
            column.Rows.AddToClassList("hud-scoreboard__rows");
            element.Add(column.Rows);

            _columnsParent.Add(element);
            _columns.Add(column);
            return column;
        }

        private static Row TakeRow(Column column)
        {
            if (column.Used < column.RowPool.Count) return column.RowPool[column.Used++];
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
            column.Rows.Add(element);
            column.RowPool.Add(row);
            column.Used++;
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
