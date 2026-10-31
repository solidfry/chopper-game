using System.Collections.Generic;
using Chopper.Presentation.Bridge;
using Chopper.Presentation.Hangar;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Chopper.UI.Hangar
{
    /// <summary>
    /// Shows the <see cref="HangarModel"/> in <c>Hangar.uxml</c>: chassis tabs, the chassis's slots (click = focus the
    /// slot), the focused slot's modules (hover = stat preview, click = fit), stat bars, skin, launch. Escape or gamepad
    /// B goes back to the overview. Rebuilds the lists on <see cref="HangarModel.Changed"/>; on
    /// <see cref="HangarModel.StatsChanged"/> only the bars move (so hovering does not rebuild the buttons under the pointer).
    /// </summary>
    [RequireComponent(typeof(PanelRenderer))]
    public sealed class HangarView : MonoBehaviour
    {
        private const string ActiveTabClass = "hangar-tab--active";
        private const string SelectedClass = "hangar-item--selected";
        private const string EmptyClass = "hangar-item--empty";
        private const string LockedClass = "hangar-item--locked";
        private const string WorseClass = "hangar-stat__delta--worse";
        private const string OverClass = "hangar-stat--over";
        private const string CancelClass = "hangar__button--cancel";
        private const string FindMatchText = "[ FIND MATCH ]";
        private const string CancelSearchText = "[ CANCEL SEARCH ]";

        [SerializeField, Tooltip("Scene presentation bridge; it owns the hangar model.")]
        private PresentationBridge bridge;

        private HangarModel _model;
        private PanelRenderer _renderer;
        private VisualElement _root;
        private VisualElement _tabs;
        private Label _chassisName;
        private Label _chassisDescription;
        private VisualElement _slots;
        private VisualElement _stats;
        private VisualElement _weightRow;
        private VisualElement _weightFill;
        private VisualElement _weightDelta;
        private Label _weightText;
        private Label _focusTitle;
        private VisualElement _options;
        private DropdownField _skin;
        private Toggle _paintView;
        private Label _error;
        private Button _launch;
        private Button _findMatch;
        private Label _matchStatus;
        /// <summary>Unscaled time the current search started; -1 when not searching.</summary>
        private float _searchStart = -1f;
        private int _shownSeconds = -1;
        private readonly List<(VisualElement Fill, VisualElement Delta, Label Text)> _bars = new();

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetBridge(PresentationBridge value) => bridge = value;

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
            _tabs = _root.Q("chassis-tabs");
            _chassisName = _root.Q<Label>("chassis-name");
            _chassisDescription = _root.Q<Label>("chassis-description");
            _slots = _root.Q("slots");
            _stats = _root.Q("stats");
            _weightRow = _root.Q("weight-track").parent;
            _weightFill = _root.Q("weight-fill");
            _weightDelta = _root.Q("weight-delta");
            _weightText = _root.Q<Label>("weight-text");
            _focusTitle = _root.Q<Label>("focus-title");
            _options = _root.Q("options");
            _skin = _root.Q<DropdownField>("skin");
            _paintView = _root.Q<Toggle>("paint-view");
            _error = _root.Q<Label>("error");
            _launch = _root.Q<Button>("launch");
            _findMatch = _root.Q<Button>("find-match");
            _matchStatus = _root.Q<Label>("match-status");

            _skin.choices = new List<string>(_model.SkinNames);
            _skin.RegisterValueChangedCallback(OnSkinChanged);
            _paintView.RegisterValueChangedCallback(OnPaintViewChanged);
            _launch.clicked += OnLaunch;
            _findMatch.clicked += OnFindMatch;
            _model.MatchStatusChanged += RefreshMatch;
            Refresh();
            _launch.Focus();
        }

        private void Unbind()
        {
            if (_root == null) return;
            _skin.UnregisterValueChangedCallback(OnSkinChanged);
            _paintView.UnregisterValueChangedCallback(OnPaintViewChanged);
            _launch.clicked -= OnLaunch;
            _findMatch.clicked -= OnFindMatch;
            _model.MatchStatusChanged -= RefreshMatch;
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
            BuildTabs();
            _chassisName.text = _model.ChassisIndex < _model.ChassisNames.Count ? _model.ChassisNames[_model.ChassisIndex] : "";
            _chassisDescription.text = _model.ChassisDescription;
            BuildSlots();
            BuildOptions();
            BuildStats();

            if (_model.SkinIndex < _model.SkinNames.Count) _skin.SetValueWithoutNotify(_model.SkinNames[_model.SkinIndex]);
            _skin.SetEnabled(_model.SkinNames.Count > 0);
            _paintView.SetValueWithoutNotify(_model.PaintView);
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

        private void BuildTabs()
        {
            _tabs.Clear();
            for (var i = 0; i < _model.ChassisNames.Count; i++)
            {
                var index = i;
                var tab = new Button(() => _model.SelectChassis(index)) { text = _model.ChassisNames[i] };
                tab.AddToClassList("hangar-tab");
                tab.AddToClassList("chopper-title");
                tab.EnableInClassList(ActiveTabClass, i == _model.ChassisIndex);
                _tabs.Add(tab);
            }
        }

        private void BuildSlots()
        {
            _slots.Clear();
            for (var i = 0; i < _model.Slots.Count; i++)
            {
                var slot = i;
                var row = _model.Slots[i];
                var item = Item(row.Label, row.Module, () => _model.FocusSlot(slot));
                item.EnableInClassList(SelectedClass, i == _model.FocusedSlot);
                item.EnableInClassList(EmptyClass, row.Empty);
                _slots.Add(item);
            }
        }

        private void BuildOptions()
        {
            _options.Clear();
            var focused = _model.FocusedSlot != HangarModel.Overview;
            _focusTitle.text = focused ? $"> {_model.FocusTitle}_" : "> SELECT A SLOT_";
            if (!focused) return;

            for (var i = 0; i < _model.Options.Count; i++)
            {
                var option = i;
                var row = _model.Options[i];
                var item = Item(row.Name, row.Fitted ? "FITTED" : row.Detail, () => _model.Fit(option));
                item.EnableInClassList(SelectedClass, row.Fitted);
                item.EnableInClassList(LockedClass, row.Locked);
                item.RegisterCallback<PointerEnterEvent>(_ => _model.Hover(option));
                item.RegisterCallback<PointerLeaveEvent>(_ => _model.Hover(-1));
                item.RegisterCallback<FocusInEvent>(_ => _model.Hover(option));
                _options.Add(item);
            }
        }

        private static Button Item(string name, string value, System.Action onClick)
        {
            var item = new Button(onClick);
            item.AddToClassList("hangar-item");
            var label = new Label(name);
            label.AddToClassList("hangar-item__name");
            var detail = new Label(value);
            detail.AddToClassList("hangar-item__value");
            item.Add(label);
            item.Add(detail);
            return item;
        }

        private void BuildStats()
        {
            _stats.Clear();
            _bars.Clear();
            foreach (var stat in _model.Stats)
            {
                var row = new VisualElement();
                row.AddToClassList("hangar-stat");
                var label = new Label(stat.Label);
                label.AddToClassList("hangar-stat__label");
                var track = new VisualElement();
                track.AddToClassList("hangar-stat__track");
                var fill = new VisualElement();
                fill.AddToClassList("hangar-stat__fill");
                var delta = new VisualElement();
                delta.AddToClassList("hangar-stat__delta");
                track.Add(fill);
                track.Add(delta);
                var text = new Label();
                text.AddToClassList("hangar-stat__text");
                row.Add(label);
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
            for (var i = 0; i < _bars.Count && i < _model.Stats.Count; i++)
            {
                var stat = _model.Stats[i];
                var (fill, delta, text) = _bars[i];
                SetBar(fill, delta, stat.Fraction, stat.PreviewFraction, higherIsBetter: true);
                text.text = stat.Text;
            }

            SetBar(_weightFill, _weightDelta, _model.WeightFraction, _model.PreviewWeightFraction, higherIsBetter: false);
            _weightRow.EnableInClassList(OverClass, _model.OverWeight);
            _weightText.text = _model.WeightText;
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

        private void OnSkinChanged(ChangeEvent<string> evt)
        {
            var index = _skin.choices.IndexOf(evt.newValue);
            if (index >= 0) _model.SelectSkin(index);
        }

        private void OnPaintViewChanged(ChangeEvent<bool> evt) => _model.SetPaintView(evt.newValue);

        private void OnLaunch() => _model.Launch();

        private void OnFindMatch()
        {
            if (_model.Searching) _model.CancelSearch();
            else _model.FindMatch();
        }
    }
}
