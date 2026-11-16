using Chopper.Presentation.Flow;
using UnityEngine;
using UnityEngine.UIElements;

namespace Chopper.UI.Transitions
{
    /// <summary>
    /// Draws the scene flow's transition (<see cref="TransitionModel"/>): the <see cref="DigitalWipe"/> blocks, and the
    /// destination and status while covered. Lives on the scene flow's persistent object with its own
    /// <see cref="PanelRenderer"/> (made by the root scope's entry point), so it stays up while scenes change.
    /// While visible it blocks clicks, so nothing starts a second scene change.
    /// </summary>
    [RequireComponent(typeof(PanelRenderer))]
    public sealed class SceneTransitionView : MonoBehaviour
    {
        private TransitionModel _model;
        private SceneTransitionSettings _settings;
        private PanelRenderer _renderer;
        private VisualElement _root;
        private DigitalWipe _wipe;
        private VisualElement _info;
        private Label _status;
        private Label _label;
        private Label _code;
        private VisualElement _briefing;
        private Label _briefMode, _briefTagline, _briefRules, _briefMap, _briefConditions, _briefPlayers, _briefTip;
        private MatchBriefing _shownBriefing;
        private int _briefVersion = -1;
        private int _typed = -1;
        private bool _shown;
        private float _infoOpacity = -1f;
        private int _codeStep = -1;

        /// <summary>Call before the object is active.</summary>
        public void Bind(TransitionModel model, SceneTransitionSettings settings)
        {
            _model = model;
            _settings = settings;
        }

        private void OnEnable()
        {
            _renderer = GetComponent<PanelRenderer>();
            _renderer.RegisterUIReloadCallback(OnUIReload);
        }

        private void OnDisable()
        {
            if (_renderer != null) _renderer.UnregisterUIReloadCallback(OnUIReload);
            _root = null;
        }

        private void OnUIReload(PanelRenderer panelRenderer, VisualElement rootElement, int version)
        {
            _root = rootElement?.Q("transition");
            if (_root == null) return;
            _wipe = _root.Q<DigitalWipe>("wipe");
            _info = _root.Q("info");
            _status = _root.Q<Label>("status");
            _label = _root.Q<Label>("label");
            _code = _root.Q<Label>("code");
            _briefing = _root.Q("briefing");
            _briefMode = _root.Q<Label>("briefing-mode");
            _briefTagline = _root.Q<Label>("briefing-tagline");
            _briefRules = _root.Q<Label>("briefing-rules");
            _briefMap = _root.Q<Label>("briefing-map");
            _briefConditions = _root.Q<Label>("briefing-conditions");
            _briefPlayers = _root.Q<Label>("briefing-players");
            _briefTip = _root.Q<Label>("briefing-tip");
            _shownBriefing = null;
            _briefVersion = -1;
            _typed = -1;
            _shown = false;
            _infoOpacity = -1f;
            _root.EnableInClassList("transition--hidden", true);
        }

        /// <summary>Each frame it shows, so changes to the settings asset show at once while tuning in play mode.</summary>
        private void ApplySettings()
        {
            if (_wipe == null || _settings == null) return;
            _wipe.blockSize = _settings.BlockSize;
            _wipe.edgeWidth = _settings.EdgeWidth;
            _wipe.sweep = _settings.Sweep;
            _wipe.sweepAngle = _settings.SweepAngle;
            _wipe.frontFlickerRate = _settings.FrontFlickerRate;
            _wipe.glitchBars = _settings.GlitchBars;
            _wipe.glitchStrength = _settings.GlitchStrength;
            _wipe.scanLines = _settings.ScanLines;
            _wipe.background = _settings.Background;
            _wipe.edge = _settings.Edge;
            _wipe.accent = _settings.Accent;
            _wipe.glitch = _settings.Glitch;
        }

        private void LateUpdate()
        {
            if (_root == null || _root.panel == null || _model == null) return;

            var visible = _model.Visible;
            if (visible != _shown)
            {
                _shown = visible;
                _root.EnableInClassList("transition--hidden", !visible);
                _root.pickingMode = visible ? PickingMode.Position : PickingMode.Ignore;
            }
            if (!visible) return;

            ApplySettings();
            _wipe?.Set(_model.Cover, _model.Revealing, _model.Seed, _model.Time);

            // The text shows only once the screen is nearly covered.
            var opacity = Mathf.Round(Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.75f, 1f, _model.Cover)) * 20f) / 20f;
            if (!Mathf.Approximately(opacity, _infoOpacity))
            {
                _infoOpacity = opacity;
                if (_info != null) _info.style.opacity = opacity;
                if (_code != null) _code.style.opacity = opacity;
                if (_briefing != null) _briefing.style.opacity = opacity;
            }
            if (_status != null && _status.text != _model.Status) _status.text = _model.Status;
            if (_label != null && _label.text != _model.Label) _label.text = _model.Label;
            UpdateCode();
            UpdateBriefing();
        }

        /// <summary>The briefing: shown with the destination; its lines type in once the screen is covered.</summary>
        private void UpdateBriefing()
        {
            if (_briefing == null) return;
            var briefing = _model.Briefing;
            if (briefing != _shownBriefing)
            {
                _shownBriefing = briefing;
                _briefVersion = -1;
                _typed = -1;
                _briefing.EnableInClassList("briefing--hidden", briefing == null);
                _info?.EnableInClassList("transition__info--briefing", briefing != null);
            }
            if (briefing == null) return;

            // Characters shown so far: about 90 a second from fully covered (all at once when revealing).
            var since = _model.Time - (_settings != null ? _settings.CoverSeconds : 0.55f);
            var typed = _model.Revealing ? int.MaxValue : Mathf.Max(0, (int)(since * 90f));
            if (typed == _typed && briefing.Version == _briefVersion) return;
            _typed = typed;
            _briefVersion = briefing.Version;

            // Typed in order: mode, tagline, rules, map, conditions, players, tip.
            var left = typed;
            Type(_briefMode, briefing.Mode, ref left);
            Type(_briefTagline, briefing.Tagline, ref left);
            Type(_briefRules, briefing.Rules, ref left);
            Type(_briefMap, briefing.Map, ref left);
            Type(_briefConditions, string.IsNullOrEmpty(briefing.Conditions) ? "CONDITIONS // AWAITING SERVER" : briefing.Conditions, ref left);
            Type(_briefPlayers, briefing.Players, ref left);
            Type(_briefTip, briefing.Tip, ref left);
        }

        /// <summary>Shows up to <paramref name="left"/> characters of <paramref name="text"/> (an underscore cursor while typing).</summary>
        private static void Type(Label label, string text, ref int left)
        {
            if (label == null) return;
            text ??= "";
            string shown;
            if (left >= text.Length) shown = text;
            else if (left <= 0) shown = "";
            else shown = text.Substring(0, left) + "_";
            left -= text.Length;
            if (label.text != shown) label.text = shown;
            label.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>Corner read-out: a fake link address that ticks about 10 times a second.</summary>
        private void UpdateCode()
        {
            if (_code == null) return;
            var step = (int)(_model.Time * 10f);
            if (step == _codeStep) return;
            _codeStep = step;
            unchecked
            {
                var h = (uint)(_model.Seed * 2654435761u + (uint)step * 40503u);
                _code.text = $"NODE {h % 97:00} // {(h >> 8) & 0xFFFF:X4}:{(h >> 4) & 0xFFF:X3} // SEQ {step:0000}";
            }
        }
    }
}
