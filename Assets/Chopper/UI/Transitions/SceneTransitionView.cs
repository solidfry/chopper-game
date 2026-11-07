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
            }
            if (_status != null && _status.text != _model.Status) _status.text = _model.Status;
            if (_label != null && _label.text != _model.Label) _label.text = _model.Label;
            UpdateCode();
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
