using Chopper.Presentation.Bridge;
using Chopper.Presentation.Hud;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace Chopper.UI.Cockpit
{
    /// <summary>
    /// Live cockpit screens (<c>CockpitScreens.uxml</c>): flight, tactical and systems pages bound to the same view models
    /// as the HUD (<see cref="ChopperHudModel"/>, <see cref="WorldMarkerModel"/>), so there is one data path. Its panel
    /// settings draw into a render texture that the kit's screen material reads (Chopper → Setup makes both); the cockpit
    /// models' screen faces pick their page by UV. Drawn only in a cockpit view while you have a chopper.
    /// </summary>
    [RequireComponent(typeof(PanelRenderer))]
    public sealed class CockpitScreensView : MonoBehaviour
    {
        private const string HiddenClass = "screens--hidden";
        private const string LockedClass = "tactical__lock--locked";
        private const string LowClass = "systems--low";
        private const string FlaresReadyClass = "systems__flares--ready";

        [SerializeField, Tooltip("Scene presentation bridge; it owns the view models and the camera rig.")]
        private PresentationBridge bridge;

        private PresentationContext _context;
        private ChopperHudModel _model;
        private PanelRenderer _renderer;
        private VisualElement _root;
        private VisualElement _systems;
        private Label _heading;
        private Label _lock;
        private Label _threat;
        private Label _flares;
        private Label _warning;
        private RadarScope _radar;
        private bool _visible = true;
        private int _shownHeading = -1;

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetBridge(PresentationBridge value) => bridge = value;

        private void OnEnable()
        {
            if (bridge == null)
            {
                Debug.LogError($"{name}: no presentation bridge set. Run Chopper → Setup.", this);
                enabled = false;
                return;
            }
            _context = bridge.Context;
            _model = _context.Hud;
            _context.Markers.Changed += OnMarkersChanged;
            _renderer = GetComponent<PanelRenderer>();
            // Called at once if the tree exists, and again after each rebuild (UXML hot reload).
            _renderer.RegisterUIReloadCallback(OnUIReload);
        }

        private void OnDisable()
        {
            if (_renderer != null) _renderer.UnregisterUIReloadCallback(OnUIReload);
            if (_context != null) _context.Markers.Changed -= OnMarkersChanged;
            Unbind();
        }

        private void OnUIReload(PanelRenderer panelRenderer, VisualElement rootElement, int version)
        {
            Unbind();
            _root = rootElement?.Q("screens");
            if (_root == null) return;

            _root.dataSource = _model;
            _heading = _root.Q<Label>("heading");
            _lock = _root.Q<Label>("lock");
            _threat = _root.Q<Label>("threat");
            _flares = _root.Q<Label>("flares");
            _warning = _root.Q<Label>("warning");
            _systems = _root.Q("systems");
            _radar = _root.Q<RadarScope>("radar");

            BindNumber(_root.Q<Label>("speed"), nameof(ChopperHudModel.SpeedText));
            BindNumber(_root.Q<Label>("altitude"), nameof(ChopperHudModel.AltitudeText));
            BindNumber(_root.Q<Label>("hull-value"), nameof(ChopperHudModel.HealthText));
            Bind(_root.Q<Label>("weapon"), "text", nameof(ChopperHudModel.PylonText));
            var attitude = _root.Q<AttitudeIndicator>("attitude");
            Bind(attitude, nameof(AttitudeIndicator.roll), nameof(ChopperHudModel.RollDegrees));
            Bind(attitude, nameof(AttitudeIndicator.pitch), nameof(ChopperHudModel.PitchDegrees));
            Bind(_root.Q<SegmentBar>("hull"), nameof(SegmentBar.value), nameof(ChopperHudModel.HealthFraction));
            Bind(_root.Q<SegmentBar>("boost"), nameof(SegmentBar.value), nameof(ChopperHudModel.BoostEnergy));

            _model.propertyChanged += OnModelChanged;
            _visible = !_root.ClassListContains(HiddenClass);
            Refresh();
            OnMarkersChanged();
        }

        private void Unbind()
        {
            if (_root == null) return;
            _model.propertyChanged -= OnModelChanged;
            _root.dataSource = null;
            _root = null;
            _radar = null;
            _shownHeading = -1;
        }

        private void Update()
        {
            if (_root == null) return;
            // Only in a cockpit view with a chopper: otherwise the pages are not seen, so they are not drawn.
            var rig = _context.CameraRig;
            var visible = rig != null && rig.HidesOwnChopper && _model.HasChopper;
            if (visible == _visible) return;
            _visible = visible;
            _root.EnableInClassList(HiddenClass, !visible);
        }

        private void OnModelChanged(object sender, BindablePropertyChangedEventArgs args) => Refresh();

        /// <summary>The few texts and classes the bindings cannot express.</summary>
        private void Refresh()
        {
            if (_root == null) return;
            var heading = Mathf.RoundToInt(_model.HeadingDegrees) % 360;
            if (heading != _shownHeading)
            {
                _shownHeading = heading;
                _heading.text = $"HDG {heading:000}";
            }
            _lock.text = _model.Locked ? "LOCK" : _model.LockProgress > 0f ? "ACQ" : "SCAN";
            _lock.EnableInClassList(LockedClass, _model.Locked);
            _threat.text = _model.MissileInbound ? (_model.WarningBlink ? "MISSILE" : "")
                : _model.ThreatLocked ? "RWR LOCK"
                : _model.ThreatLocking ? "RWR" : "";
            _radar?.SetMissile(_model.MissileInbound, _model.MissileDirectionDegrees);
            _systems.EnableInClassList(LowClass, _model.HealthLow);
            _flares.text = _model.FlaresReady ? "FLR READY" : "FLR ---";
            _flares.EnableInClassList(FlaresReadyClass, _model.FlaresReady);
            _warning.text = _model.OutOfBounds ? "BOUNDS" : _model.HealthLow ? "HULL LOW" : "";
        }

        private void OnMarkersChanged()
        {
            if (_radar != null && _visible) _radar.SetContacts(_context.Markers.Markers);
        }

        private static void Bind(VisualElement element, string targetProperty, string modelProperty)
        {
            element.SetBinding(targetProperty, new DataBinding
            {
                dataSourcePath = new PropertyPath(modelProperty),
                bindingMode = BindingMode.ToTarget,
            });
        }

        /// <summary>Text binding with fixed-width digits, so changing numbers do not jump.</summary>
        private static void BindNumber(Label label, string modelProperty)
        {
            var binding = new DataBinding
            {
                dataSourcePath = new PropertyPath(modelProperty),
                bindingMode = BindingMode.ToTarget,
            };
            binding.sourceToUiConverters.AddConverter((ref string value) => TabularText.Wrap(value));
            label.SetBinding("text", binding);
        }
    }
}
