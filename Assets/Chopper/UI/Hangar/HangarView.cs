using System.Collections.Generic;
using Chopper.Presentation.Bridge;
using Chopper.Presentation.Hangar;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Chopper.UI.Hangar
{
    /// <summary>
    /// Shows the <see cref="HangarModel"/> in <c>Hangar.uxml</c> (plan section 5, "New layout").
    /// <list type="bullet">
    /// <item>Overview: a callout card per socket, linked by a line to the socket on screen (from the
    /// <see cref="HangarStage"/> each frame); chassis cards and saved builds on the left; slot tiles along the bottom.
    /// A card or a tile opens the slot.</item>
    /// <item>Slot open: "SELECT &lt;SLOT&gt;", the hovered (else fitted) module's detail on the left, the slot's modules as
    /// tiles along the bottom (hover = stat preview, click = fit). Back, Escape or gamepad B closes it.</item>
    /// <item>Always: stats float on the right (big numbers and bars with the before/after change), skin, launch.</item>
    /// </list>
    /// Rebuilds on <see cref="HangarModel.Changed"/>; on <see cref="HangarModel.StatsChanged"/> only the stats and the
    /// detail change (so hovering does not rebuild the tiles under the pointer).
    /// </summary>
    [RequireComponent(typeof(PanelRenderer))]
    public sealed class HangarView : MonoBehaviour
    {
        private const string SelectedClass = "hangar-tile--selected";
        private const string FittedClass = "hangar-tile--fitted";
        private const string SkinSelectedClass = "hangar-skin--selected";
        private const string SkinLockedClass = "hangar-skin--locked";
        /// <summary>Colour bands in a skin swatch (patterns have up to four colours).</summary>
        private const int MaxSwatchColours = 4;
        private const string EmptyClass = "hangar-tile--empty";
        private const string LockedClass = "hangar-tile--locked";
        private const string MarkEquippedClass = "hangar-tile__mark--equipped";
        private const string MarkLockedClass = "hangar-tile__mark--locked";
        private const string MarkEmptyClass = "hangar-tile__mark--empty";
        /// <summary>No state: the tag keeps its space (names stay at the same height) but does not show.</summary>
        private const string HiddenMarkClass = "hangar-tile__mark--none";
        private const string ActiveCardClass = "hangar-chassis--active";
        private const string CalloutEmptyClass = "hangar-callout--empty";
        private const string CalloutRightClass = "hangar-callout--right";
        private const string WorseClass = "hangar-stat__delta--worse";
        private const string BetterValueClass = "hangar-headline__value--better";
        private const string WorseValueClass = "hangar-headline__value--worse";
        private const string OverClass = "hangar-stat--over";
        private const string CancelClass = "hangar__button--cancel";
        private const string HiddenClass = "hangar--hidden";
        private const string TagEquippedClass = "hangar__tag--equipped";
        private const string TagLockedClass = "hangar__tag--locked";
        private const string FindMatchText = "[ FIND MATCH ]";
        private const string CancelSearchText = "[ CANCEL SEARCH ]";
        private const string NoPreset = "-";
        /// <summary>Pixels between the chopper's widest socket and the card column.</summary>
        private const float CalloutReach = 70f;
        private const float CalloutGap = 6f;
        /// <summary>Pixels the side columns take (margin + 380 px column + a gap): cards stay between them.</summary>
        private const float SideColumn = 418f;
        /// <summary>Card movement smoothing (per second); cards glide when the chopper turns.</summary>
        private const float CalloutFollowRate = 10f;
        /// <summary>Gamepad right stick dead zone for turning the chopper.</summary>
        private const float StickDeadZone = 0.15f;

        [SerializeField, Tooltip("Scene presentation bridge; it owns the hangar model.")]
        private PresentationBridge bridge;
        [SerializeField, Tooltip("The 3D view: gives the socket positions on screen for the callouts.")]
        private HangarStage stage;

        private HangarModel _model;
        private PanelRenderer _renderer;
        private VisualElement _root;
        private VisualElement _callouts;
        private CalloutLines _calloutLines;
        private VisualElement _overviewPanel;
        private VisualElement _focusPanel;
        private Label _chassisName;
        private Label _chassisDescription;
        private VisualElement _chassisGrid;
        private DropdownField _preset;
        private TextField _presetName;
        private Button _presetSave;
        private Button _presetRename;
        private Button _presetDelete;
        private Button _back;
        private Label _focusTitle;
        private Label _detailName;
        private Label _detailDescription;
        private Label _detailState;
        private Label _detailWeight;
        private VisualElement _headline;
        private VisualElement _stats;
        private VisualElement _weightRow;
        private VisualElement _weightFill;
        private VisualElement _weightDelta;
        private Label _weightText;
        private VisualElement _skins;
        private Label _tilesTitle;
        private VisualElement _tiles;
        private Label _error;
        private Button _launch;
        private Button _findMatch;
        private Label _matchStatus;
        /// <summary>Unscaled time the current search started; -1 when not searching.</summary>
        private float _searchStart = -1f;
        private int _shownSeconds = -1;
        /// <summary>Preset picked in the dropdown (-1 = none); the target of rename and delete.</summary>
        private int _presetIndex = -1;
        /// <summary>The left mouse button went down on the empty view (not on UI) and is still held.</summary>
        private bool _dragging;
        private readonly List<(VisualElement Fill, VisualElement Delta, Label Text)> _bars = new();
        private readonly List<(Label Value, Label Unit)> _headlines = new();
        private readonly List<Callout> _cards = new();
        private readonly List<int> _left = new();
        private readonly List<int> _right = new();

        private sealed class Callout
        {
            public Button Card;
            public Vector2 Socket;
            public Vector2 Position;
            public Vector2 Target;
            public bool Visible;
            public bool Placed;
            public bool Right;
        }

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetBridge(PresentationBridge value) => bridge = value;

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetStage(HangarStage value) => stage = value;

        private void OnEnable()
        {
            if (bridge == null)
            {
                Debug.LogError($"{name}: no presentation bridge set. Run Chopper → Setup, step 7.", this);
                enabled = false;
                return;
            }
            _model = bridge.Context.Hangar;
            if (_model == null)
            {
                Debug.LogError($"{name}: the game config has no loadout catalog. Run Chopper → Setup.", this);
                enabled = false;
                return;
            }
            if (stage == null) stage = FindAnyObjectByType<HangarStage>();
            if (stage == null) Debug.LogWarning($"{name}: no hangar stage set: no callouts. Run Chopper → Setup, step 7.", this);
            _model.Changed += Refresh;
            _model.StatsChanged += RefreshStats;
            _renderer = GetComponent<PanelRenderer>();
            _renderer.RegisterUIReloadCallback(OnUIReload);
        }

        private void OnDisable()
        {
            if (_model != null)
            {
                _model.Changed -= Refresh;
                _model.StatsChanged -= RefreshStats;
            }
            if (_renderer != null) _renderer.UnregisterUIReloadCallback(OnUIReload);
            Unbind();
        }

        private void OnUIReload(PanelRenderer panelRenderer, VisualElement rootElement, int version)
        {
            Unbind();
            _root = rootElement?.Q("hangar");
            if (_root == null) return;
            _callouts = _root.Q("callouts");
            _calloutLines = _root.Q<CalloutLines>("callout-lines");
            _overviewPanel = _root.Q("overview-panel");
            _focusPanel = _root.Q("focus-panel");
            _chassisName = _root.Q<Label>("chassis-name");
            _chassisDescription = _root.Q<Label>("chassis-description");
            _chassisGrid = _root.Q("chassis-grid");
            _preset = _root.Q<DropdownField>("preset");
            _presetName = _root.Q<TextField>("preset-name");
            _presetSave = _root.Q<Button>("preset-save");
            _presetRename = _root.Q<Button>("preset-rename");
            _presetDelete = _root.Q<Button>("preset-delete");
            _back = _root.Q<Button>("back");
            _focusTitle = _root.Q<Label>("focus-title");
            _detailName = _root.Q<Label>("detail-name");
            _detailDescription = _root.Q<Label>("detail-description");
            _detailState = _root.Q<Label>("detail-state");
            _detailWeight = _root.Q<Label>("detail-weight");
            _headline = _root.Q("stats-headline");
            _stats = _root.Q("stats");
            _weightRow = _root.Q("weight-track").parent;
            _weightFill = _root.Q("weight-fill");
            _weightDelta = _root.Q("weight-delta");
            _weightText = _root.Q<Label>("weight-text");
            _skins = _root.Q("skins");
            _tilesTitle = _root.Q<Label>("tiles-title");
            _tiles = _root.Q("tiles");
            _error = _root.Q<Label>("error");
            _launch = _root.Q<Button>("launch");
            _findMatch = _root.Q<Button>("find-match");
            _matchStatus = _root.Q<Label>("match-status");

            _preset.RegisterValueChangedCallback(OnPresetChanged);
            _presetSave.clicked += OnPresetSave;
            _presetRename.clicked += OnPresetRename;
            _presetDelete.clicked += OnPresetDelete;
            _back.clicked += OnBack;
            _launch.clicked += OnLaunch;
            _findMatch.clicked += OnFindMatch;
            _model.MatchStatusChanged += RefreshMatch;
            Refresh();
            _launch.Focus();
        }

        private void Unbind()
        {
            if (_root == null) return;
            _preset.UnregisterValueChangedCallback(OnPresetChanged);
            _presetSave.clicked -= OnPresetSave;
            _presetRename.clicked -= OnPresetRename;
            _presetDelete.clicked -= OnPresetDelete;
            _back.clicked -= OnBack;
            _launch.clicked -= OnLaunch;
            _findMatch.clicked -= OnFindMatch;
            _model.MatchStatusChanged -= RefreshMatch;
            _cards.Clear();
            _root = null;
        }

        private void Update()
        {
            var escape = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
            var back = Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame;
            if (escape || back)
            {
                // Back out of a slot first: the loadout can be changed while a search runs.
                if (_model.Back()) (_model.Searching ? _findMatch : _launch)?.Focus();
                else if (_model.Searching) _model.CancelSearch();
            }
            TickSearchTime();
            TurnChopper();
            PlaceCallouts(Time.unscaledDeltaTime);
        }

        /// <summary>
        /// Mouse drag on the empty view (not on a panel, card or tile) turns the chopper; so does the gamepad right
        /// stick.
        /// </summary>
        private void TurnChopper()
        {
            if (stage == null) return;
            var mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.leftButton.wasPressedThisFrame && !PointerOverUI(mouse.position.ReadValue())) _dragging = true;
                if (!mouse.leftButton.isPressed) _dragging = false;
                if (_dragging) stage.Drag(mouse.delta.ReadValue().x);
            }
            else _dragging = false;
            stage.Holding = _dragging;

            var pad = Gamepad.current;
            if (pad == null) return;
            var axis = pad.rightStick.ReadValue().x;
            if (Mathf.Abs(axis) > StickDeadZone) stage.Steer(axis);
        }

        /// <summary>True when the screen point (pixels, y up) is on a hangar control or panel.</summary>
        private bool PointerOverUI(Vector2 screen)
        {
            if (_root?.panel == null) return false;
            var point = RuntimePanelUtils.ScreenToPanel(_root.panel, new Vector2(screen.x, Screen.height - screen.y));
            var picked = _root.panel.Pick(point);
            return picked != null && picked != _root && _root.Contains(picked);
        }

        /// <summary>Updates the elapsed search time once a second.</summary>
        private void TickSearchTime()
        {
            if (_root == null || _searchStart < 0f) return;
            var seconds = (int)(Time.unscaledTime - _searchStart);
            if (seconds == _shownSeconds) return;
            _shownSeconds = seconds;
            _matchStatus.text = $"{_model.MatchStatus} {seconds / 60}:{seconds % 60:00}";
        }

        private void Refresh()
        {
            if (_root == null) return;
            var focused = _model.FocusedSlot != HangarModel.Overview;
            _overviewPanel.EnableInClassList(HiddenClass, focused);
            _focusPanel.EnableInClassList(HiddenClass, !focused);
            _callouts.EnableInClassList(HiddenClass, focused);

            _chassisName.text = _model.ChassisIndex < _model.ChassisNames.Count ? _model.ChassisNames[_model.ChassisIndex] : "";
            _chassisDescription.text = _model.ChassisDescription;
            _focusTitle.text = focused ? $"SELECT {_model.FocusTitle}" : "";
            BuildChassisCards();
            BuildPresets();
            BuildCallouts();
            BuildTiles();
            BuildStats();
            RefreshDetail();

            BuildSkins();
            _error.text = _model.Error;
            RefreshMatch();
        }

        /// <summary>
        /// While a search runs: training is off, FIND MATCH becomes CANCEL SEARCH, and the status line shows the step
        /// and the time searched. The rest of the hangar stays usable.
        /// </summary>
        private void RefreshMatch()
        {
            if (_root == null) return;
            var searching = _model.Searching;
            if (searching && _searchStart < 0f) _searchStart = Time.unscaledTime;
            if (!searching) _searchStart = -1f;
            _shownSeconds = -1;
            _launch.SetEnabled(_model.CanLaunch && !searching);
            _findMatch.SetEnabled(searching || _model.CanLaunch);
            _findMatch.text = searching ? CancelSearchText : FindMatchText;
            _findMatch.EnableInClassList(CancelClass, searching);
            _matchStatus.text = _model.MatchStatus;
            TickSearchTime();
        }

        // ---- Overview: chassis cards and saved builds ----

        private void BuildChassisCards()
        {
            _chassisGrid.Clear();
            for (var i = 0; i < _model.ChassisCards.Count; i++)
            {
                var index = i;
                var data = _model.ChassisCards[i];
                var card = new Button(() => _model.SelectChassis(index));
                card.AddToClassList("hangar-chassis");
                card.EnableInClassList(ActiveCardClass, i == _model.ChassisIndex);
                card.Add(Text(data.Name, "hangar-chassis__name", title: true));
                card.Add(Text(data.Detail, "hangar-chassis__detail"));
                _chassisGrid.Add(card);
            }
        }

        private void BuildPresets()
        {
            var choices = new List<string> { NoPreset };
            choices.AddRange(_model.PresetNames);
            _preset.choices = choices;
            if (_presetIndex >= _model.PresetNames.Count) _presetIndex = -1;
            _preset.SetValueWithoutNotify(_presetIndex >= 0 ? _model.PresetNames[_presetIndex] : NoPreset);
            _presetRename.SetEnabled(_presetIndex >= 0);
            _presetDelete.SetEnabled(_presetIndex >= 0);
        }

        private void OnPresetChanged(ChangeEvent<string> evt)
        {
            _presetIndex = -1;
            for (var i = 0; i < _model.PresetNames.Count; i++)
                if (_model.PresetNames[i] == evt.newValue) _presetIndex = i;
            if (_presetIndex < 0)
            {
                BuildPresets();
                return;
            }
            _presetName.SetValueWithoutNotify(_model.PresetNames[_presetIndex]);
            _model.ApplyPreset(_presetIndex);
        }

        /// <summary>Saves under the typed name, else over the picked preset, else as "BUILD n".</summary>
        private void OnPresetSave()
        {
            var name = _presetName.value;
            if (string.IsNullOrWhiteSpace(name))
                name = _presetIndex >= 0 ? _model.PresetNames[_presetIndex] : $"BUILD {_model.PresetNames.Count + 1}";
            var index = _model.SavePreset(name);
            if (index < 0) return;
            _presetIndex = index;
            _presetName.SetValueWithoutNotify(_model.PresetNames[index]);
            BuildPresets();
        }

        private void OnPresetRename()
        {
            if (_presetIndex < 0 || !_model.RenamePreset(_presetIndex, _presetName.value)) return;
            _presetName.SetValueWithoutNotify(_model.PresetNames[_presetIndex]);
            BuildPresets();
        }

        private void OnPresetDelete()
        {
            if (_presetIndex < 0) return;
            var index = _presetIndex;
            _presetIndex = -1;
            _presetName.SetValueWithoutNotify("");
            _model.DeletePreset(index);
        }

        // ---- Callouts: a card per slot, placed beside the chopper each frame ----

        private void BuildCallouts()
        {
            foreach (var callout in _cards) callout.Card.RemoveFromHierarchy();
            _cards.Clear();
            for (var i = 0; i < _model.Slots.Count; i++)
            {
                var slot = i;
                var row = _model.Slots[i];
                var card = new Button(() => _model.FocusSlot(slot));
                card.AddToClassList("hangar-callout");
                card.EnableInClassList(CalloutEmptyClass, row.Empty);
                card.Add(Text(row.Label, "hangar-callout__slot"));
                card.Add(Text(row.Empty ? "+" : row.Module, "hangar-callout__module", title: row.Empty));
                card.style.position = Position.Absolute;
                card.style.visibility = Visibility.Hidden;
                _callouts.Add(card);
                _cards.Add(new Callout { Card = card });
            }
        }

        /// <summary>
        /// Cards in two columns beside the chopper: a socket left of its centre gets a card on the left, else on the
        /// right; each column keeps the sockets' top-to-bottom order without overlap. Cards glide to their place.
        /// </summary>
        private void PlaceCallouts(float deltaTime)
        {
            if (_root == null || _calloutLines == null) return;
            _calloutLines.ClearLines();
            if (stage == null || _model.FocusedSlot != HangarModel.Overview || _cards.Count == 0) return;
            var size = _callouts.layout.size;
            if (size.x <= 0f || size.y <= 0f) return;

            var centre = ToPanel(stage.CentreViewportPoint, size);
            var reach = 0f;
            _left.Clear();
            _right.Clear();
            for (var i = 0; i < _cards.Count; i++)
            {
                var callout = _cards[i];
                callout.Visible = stage.TryGetSlotViewportPoint(i, out var viewport);
                if (!callout.Visible) continue;
                callout.Socket = ToPanel(viewport, size);
                reach = Mathf.Max(reach, Mathf.Abs(callout.Socket.x - centre.x));
                callout.Right = callout.Socket.x >= centre.x;
                (callout.Right ? _right : _left).Add(i);
            }
            reach += CalloutReach;

            Stack(_left, centre, reach, size);
            Stack(_right, centre, reach, size);

            var blend = 1f - Mathf.Exp(-CalloutFollowRate * deltaTime);
            foreach (var callout in _cards)
            {
                if (!callout.Visible) continue;
                callout.Position = callout.Placed ? Vector2.Lerp(callout.Position, callout.Target, blend) : callout.Target;
                callout.Placed = true;
            }
            // Gliding cards can meet (one moves up as the next moves down): keep the gap while they move too.
            Separate(_left);
            Separate(_right);

            foreach (var callout in _cards)
            {
                var card = callout.Card;
                card.style.visibility = callout.Visible ? Visibility.Visible : Visibility.Hidden;
                if (!callout.Visible) continue;
                card.style.left = callout.Position.x;
                card.style.top = callout.Position.y;
                card.EnableInClassList(CalloutRightClass, callout.Right);

                var height = card.layout.height > 0f ? card.layout.height : 0f;
                var width = card.layout.width > 0f ? card.layout.width : 0f;
                var edge = new Vector2(callout.Right ? callout.Position.x : callout.Position.x + width, callout.Position.y + height * 0.5f);
                _calloutLines.AddLine(callout.Socket, edge, false);
            }
            _calloutLines.Commit();
        }

        /// <summary>One column (already in socket order): each card at least a gap below the card above it.</summary>
        private void Separate(List<int> column)
        {
            var next = float.MinValue;
            foreach (var index in column)
            {
                var callout = _cards[index];
                if (callout.Position.y < next) callout.Position = new Vector2(callout.Position.x, next);
                var height = callout.Card.layout.height > 0f ? callout.Card.layout.height : 0f;
                next = callout.Position.y + height + CalloutGap;
            }
        }

        /// <summary>One column: cards in socket order, pushed down so they do not overlap, kept on screen.</summary>
        private void Stack(List<int> column, Vector2 centre, float reach, Vector2 size)
        {
            column.Sort((a, b) => _cards[a].Socket.y.CompareTo(_cards[b].Socket.y));
            var next = float.MinValue;
            foreach (var index in column)
            {
                var callout = _cards[index];
                var card = callout.Card.layout;
                var width = card.width > 0f ? card.width : 0f;
                var height = card.height > 0f ? card.height : 0f;
                var x = callout.Right ? centre.x + reach : centre.x - reach - width;
                var y = Mathf.Max(callout.Socket.y - height * 0.5f, next);
                var min = Mathf.Min(SideColumn, size.x * 0.5f - width);
                x = Mathf.Clamp(x, Mathf.Max(0f, min), Mathf.Max(min, size.x - min - width));
                y = Mathf.Clamp(y, 0f, Mathf.Max(0f, size.y - height));
                callout.Target = new Vector2(x, y);
                next = y + height + CalloutGap;
            }
        }

        private static Vector2 ToPanel(Vector2 viewport, Vector2 size) => new(viewport.x * size.x, (1f - viewport.y) * size.y);

        // ---- Tiles: slots in the overview, the slot's modules when a slot is open ----

        private void BuildTiles()
        {
            _tiles.Clear();
            var focused = _model.FocusedSlot != HangarModel.Overview;
            _tilesTitle.text = focused ? $"// {_model.FocusTitle}" : "// SLOTS";
            if (!focused)
            {
                for (var i = 0; i < _model.Slots.Count; i++)
                {
                    var slot = i;
                    var row = _model.Slots[i];
                    var tile = row.Empty
                        ? Tile(i, row.Label, "EMPTY", MarkEmptyClass, "SELECT MODULE", () => _model.FocusSlot(slot))
                        : Tile(i, row.Label, "", null, row.Module, () => _model.FocusSlot(slot));
                    tile.EnableInClassList(EmptyClass, row.Empty);
                    _tiles.Add(tile);
                }
                return;
            }

            for (var i = 0; i < _model.Options.Count; i++)
            {
                var option = i;
                var row = _model.Options[i];
                // One mark per tile, the same tag style for every state (the tile type never changes it).
                var mark = row.Locked ? "LOCKED" : row.Fitted ? "EQUIPPED" : "";
                var markClass = row.Locked ? MarkLockedClass : row.Fitted ? MarkEquippedClass : null;
                var tile = Tile(i, $"{row.Cost} KG", mark, markClass, row.Name, () => _model.Fit(option));
                tile.EnableInClassList(FittedClass, row.Fitted);
                tile.EnableInClassList(LockedClass, row.Locked);
                tile.EnableInClassList(EmptyClass, row.Empty);
                tile.RegisterCallback<PointerEnterEvent>(_ => _model.Hover(option));
                tile.RegisterCallback<PointerLeaveEvent>(_ => _model.Hover(-1));
                tile.RegisterCallback<FocusInEvent>(_ => _model.Hover(option));
                _tiles.Add(tile);
            }
        }

        /// <summary>
        /// A square tile: corner text, a state tag in the middle (EQUIPPED, LOCKED, EMPTY; none when
        /// <paramref name="markClass"/> is null), the name at the bottom. Top right: the tile number in the dot font
        /// (decoration only).
        /// </summary>
        private static Button Tile(int index, string corner, string mark, string markClass, string name, System.Action onClick)
        {
            var tile = new Button(onClick);
            tile.AddToClassList("hangar-tile");
            var serial = Text($"{index + 1:00}", "chopper-deco-dot");
            serial.AddToClassList("hangar-tile__serial");
            tile.Add(serial);
            tile.Add(Text(corner, "hangar-tile__corner"));
            var tag = Text(mark, "hangar-tile__mark");
            if (markClass != null) tag.AddToClassList(markClass);
            else tag.AddToClassList(HiddenMarkClass);
            tile.Add(tag);
            tile.Add(Text(name, "hangar-tile__name"));
            return tile;
        }

        // ---- Detail of the hovered (else fitted) module ----

        private void RefreshDetail()
        {
            if (_root == null) return;
            var index = _model.DetailOption;
            if (index < 0 || index >= _model.Options.Count)
            {
                _detailName.text = "";
                _detailDescription.text = "";
                _detailState.text = "";
                _detailWeight.text = "";
                return;
            }
            var row = _model.Options[index];
            _detailName.text = row.Name;
            _detailDescription.text = row.Description;
            _detailState.text = row.Locked ? $"LOCKED: {row.Detail}" : row.Fitted ? "EQUIPPED" : "CLICK TO FIT";
            _detailState.EnableInClassList(TagEquippedClass, row.Fitted);
            _detailState.EnableInClassList(TagLockedClass, row.Locked);
            _detailWeight.text = $"{row.Cost} KG";
        }

        // ---- Stats ----

        private void BuildStats()
        {
            _headline.Clear();
            _stats.Clear();
            _headlines.Clear();
            _bars.Clear();
            foreach (var stat in _model.Stats)
            {
                if (stat.Headline)
                {
                    var block = new VisualElement { pickingMode = PickingMode.Ignore };
                    block.AddToClassList("hangar-headline");
                    block.Add(Text(stat.Label, "hangar-headline__label"));
                    var value = Text("", "hangar-headline__value", title: true);
                    var unit = Text("", "hangar-headline__unit");
                    block.Add(value);
                    block.Add(unit);
                    _headline.Add(block);
                    _headlines.Add((value, unit));
                    continue;
                }

                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("hangar-stat");
                row.Add(Text(stat.Label, "hangar-stat__label"));
                var track = new VisualElement();
                track.AddToClassList("hangar-stat__track");
                var fill = new VisualElement();
                fill.AddToClassList("hangar-stat__fill");
                var delta = new VisualElement();
                delta.AddToClassList("hangar-stat__delta");
                track.Add(fill);
                track.Add(delta);
                var text = Text("", "hangar-stat__text");
                row.Add(track);
                row.Add(text);
                _stats.Add(row);
                _bars.Add((fill, delta, text));
            }
            RefreshStats();
        }

        private void RefreshStats()
        {
            if (_root == null) return;
            int bar = 0, headline = 0;
            foreach (var stat in _model.Stats)
            {
                if (stat.Headline)
                {
                    if (headline >= _headlines.Count) continue;
                    var (value, unit) = _headlines[headline++];
                    value.text = stat.Value;
                    unit.text = stat.Unit;
                    var change = stat.PreviewFraction - stat.Fraction;
                    value.EnableInClassList(BetterValueClass, change > 0.0005f);
                    value.EnableInClassList(WorseValueClass, change < -0.0005f);
                    continue;
                }
                if (bar >= _bars.Count) continue;
                var (fill, delta, text) = _bars[bar++];
                SetBar(fill, delta, stat.Fraction, stat.PreviewFraction, higherIsBetter: true);
                text.text = stat.Text;
            }

            SetBar(_weightFill, _weightDelta, _model.WeightFraction, _model.PreviewWeightFraction, higherIsBetter: false);
            _weightRow.EnableInClassList(OverClass, _model.OverWeight);
            _weightText.text = _model.WeightText;
            RefreshDetail();
        }

        /// <summary>Fill to the smaller value; the part between now and the preview is green (better) or red (worse).</summary>
        private static void SetBar(VisualElement fill, VisualElement delta, float now, float preview, bool higherIsBetter)
        {
            var low = Mathf.Min(now, preview);
            var high = Mathf.Max(now, preview);
            fill.style.width = Length.Percent(low * 100f);
            delta.style.left = Length.Percent(low * 100f);
            delta.style.width = Length.Percent((high - low) * 100f);
            var better = (preview >= now) == higherIsBetter;
            delta.EnableInClassList(WorseClass, !better);
        }

        private static Label Text(string text, string className, bool title = false)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(className);
            if (title) label.AddToClassList("chopper-title");
            return label;
        }

        // ---- Paint: a grid of skin tiles ----

        /// <summary>
        /// One tile per skin: a swatch of its main colours, the name, and one state line in the same tag style as the
        /// module tiles (EQUIPPED, LEVEL n when locked, else the pattern). Click = paint the chopper with it.
        /// </summary>
        private void BuildSkins()
        {
            _skins.Clear();
            for (var i = 0; i < _model.Skins.Count; i++)
            {
                var index = i;
                var row = _model.Skins[i];
                var tile = new Button(() => _model.SelectSkin(index));
                tile.AddToClassList("hangar-skin");
                tile.EnableInClassList(SkinSelectedClass, row.Selected);
                tile.EnableInClassList(SkinLockedClass, row.Locked);

                var swatch = new VisualElement { pickingMode = PickingMode.Ignore };
                swatch.AddToClassList("hangar-skin__swatch");
                var count = Mathf.Min(row.Colours.Count, MaxSwatchColours);
                for (var c = 0; c < count; c++)
                {
                    var band = new VisualElement { pickingMode = PickingMode.Ignore };
                    band.AddToClassList("hangar-skin__band");
                    var colour = row.Colours[c];
                    band.style.backgroundColor = new Color(colour.r, colour.g, colour.b, 1f);
                    swatch.Add(band);
                }
                tile.Add(swatch);
                tile.Add(Text(row.Name, "hangar-skin__name"));

                var state = row.Locked ? $"LEVEL {row.UnlockLevel}" : row.Selected ? "EQUIPPED" : row.Pattern;
                var tag = Text(state, "hangar-skin__state");
                tag.AddToClassList("hangar-tile__mark");
                if (row.Locked) tag.AddToClassList(MarkLockedClass);
                else if (row.Selected) tag.AddToClassList(MarkEquippedClass);
                else tag.AddToClassList("hangar-skin__state--plain");
                tile.Add(tag);
                _skins.Add(tile);
            }
        }

        private void OnBack()
        {
            if (_model.Back()) (_model.Searching ? _findMatch : _launch)?.Focus();
        }

        private void OnLaunch() => _model.Launch();

        private void OnFindMatch()
        {
            if (_model.Searching) _model.CancelSearch();
            else _model.FindMatch();
        }
    }
}
