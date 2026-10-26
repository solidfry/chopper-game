using Chopper.Presentation.Bridge;
using Chopper.Presentation.Hud;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace Chopper.UI.Hud
{
    /// <summary>
    /// Binds the in-flight HUD (<c>ChopperHud.uxml</c>) to the <see cref="ChopperHudModel"/>. Layout and style live in
    /// UXML/USS; this class only sets up bindings and the few class toggles that USS cannot express.
    /// The view models come from the scene's <see cref="PresentationBridge"/>.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class ChopperHudView : MonoBehaviour
    {
        [SerializeField, Tooltip("Scene presentation bridge; it owns the HUD view models.")]
        private PresentationBridge bridge;

        private const string HiddenClass = "hud--hidden";
        private const string DashReadyClass = "hud-dash--ready";
        private const string HealthLowClass = "hud-readout--low";
        private const string AimHiddenClass = "hud-aim--hidden";
        private const string HitClass = "hud-aim--hit";
        private const string KillClass = "hud-aim--kill";
        private const string WarningHiddenClass = "hud-warning--hidden";
        private const string BannerHiddenClass = "hud-match__banner--hidden";
        private const string DamageHiddenClass = "hud-damage--hidden";

        private ChopperHudModel _model;
        private MatchHudModel _match;
        private VisualElement _chopper;
        private VisualElement _boundsWarning;
        private VisualElement _damageRing;
        private VisualElement _banner;
        private VisualElement _dash;
        private VisualElement _health;
        private VisualElement _aim;

        private void OnEnable()
        {
            if (bridge == null)
            {
                Debug.LogError($"{name}: no presentation bridge set. Run Chopper → Setup.", this);
                enabled = false;
                return;
            }
            _model = bridge.Context.Hud;
            _match = bridge.Context.Match;

            var root = GetComponent<UIDocument>().rootVisualElement.Q("hud");
            _chopper = root.Q("chopper");
            _dash = _chopper.Q("dash");
            _health = _chopper.Q("health");
            _aim = _chopper.Q("aim");
            _boundsWarning = _chopper.Q("bounds-warning");
            _damageRing = _chopper.Q("damage-ring");

            _chopper.dataSource = _model;
            Bind(_chopper.Q<Label>("health-value"), "text", nameof(ChopperHudModel.HealthText));
            Bind(_chopper.Q<Label>("speed-value"), "text", nameof(ChopperHudModel.SpeedText));
            Bind(_chopper.Q<Label>("altitude-value"), "text", nameof(ChopperHudModel.AltitudeText));
            Bind(_chopper.Q<ProgressBar>("dash-bar"), "value", nameof(ChopperHudModel.DashCharge));

            var match = root.Q("match");
            _banner = match.Q("banner");
            match.dataSource = _match;
            Bind(match.Q<Label>("timer"), "text", nameof(MatchHudModel.TimerText));
            Bind(match.Q<Label>("local-score"), "text", nameof(MatchHudModel.LocalScoreText));
            Bind(match.Q<Label>("leader"), "text", nameof(MatchHudModel.LeaderText));
            Bind(match.Q<Label>("kill-feed"), "text", nameof(MatchHudModel.KillFeedText));
            Bind(_banner, "text", nameof(MatchHudModel.BannerText));

            _model.propertyChanged += OnModelChanged;
            _match.propertyChanged += OnMatchChanged;
            RefreshClasses();
            RefreshAim();
            RefreshDamageDirection();
            RefreshBanner();
        }

        private void OnDisable()
        {
            if (_model != null)
                _model.propertyChanged -= OnModelChanged;
            if (_match != null)
                _match.propertyChanged -= OnMatchChanged;
        }

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetBridge(PresentationBridge value) => bridge = value;

        private static void Bind(VisualElement element, string targetProperty, string modelProperty)
        {
            element.SetBinding(targetProperty, new DataBinding
            {
                dataSourcePath = new PropertyPath(modelProperty),
                bindingMode = BindingMode.ToTarget,
            });
        }

        private void OnModelChanged(object sender, BindablePropertyChangedEventArgs args)
        {
            if (args.propertyName == nameof(ChopperHudModel.AimViewport))
                RefreshAim();
            else if (args.propertyName == nameof(ChopperHudModel.DamageDirectionDegrees))
                RefreshDamageDirection();
            else if (args.propertyName == nameof(ChopperHudModel.HasChopper) ||
                     args.propertyName == nameof(ChopperHudModel.DashReady) ||
                     args.propertyName == nameof(ChopperHudModel.HealthLow) ||
                     args.propertyName == nameof(ChopperHudModel.OutOfBounds) ||
                     args.propertyName == nameof(ChopperHudModel.DamageIndicatorVisible) ||
                     args.propertyName == nameof(ChopperHudModel.AimVisible) ||
                     args.propertyName == nameof(ChopperHudModel.HitMarkerVisible) ||
                     args.propertyName == nameof(ChopperHudModel.KillMarkerVisible))
                RefreshClasses();
        }

        private void RefreshClasses()
        {
            _chopper.EnableInClassList(HiddenClass, !_model.HasChopper);
            _boundsWarning.EnableInClassList(WarningHiddenClass, !_model.OutOfBounds);
            _damageRing.EnableInClassList(DamageHiddenClass, !_model.DamageIndicatorVisible);
            _dash.EnableInClassList(DashReadyClass, _model.DashReady);
            _health.EnableInClassList(HealthLowClass, _model.HealthLow);
            _aim.EnableInClassList(AimHiddenClass, !_model.AimVisible);
            _aim.EnableInClassList(HitClass, _model.HitMarkerVisible);
            _aim.EnableInClassList(KillClass, _model.KillMarkerVisible);
        }

        private void RefreshDamageDirection()
        {
            _damageRing.style.rotate = new Rotate(Angle.Degrees(_model.DamageDirectionDegrees));
        }

        private void OnMatchChanged(object sender, BindablePropertyChangedEventArgs args)
        {
            if (args.propertyName == nameof(MatchHudModel.BannerVisible))
                RefreshBanner();
        }

        private void RefreshBanner() => _banner.EnableInClassList(BannerHiddenClass, !_match.BannerVisible);

        /// <summary>Viewport y goes up, panel top goes down.</summary>
        private void RefreshAim()
        {
            var viewport = _model.AimViewport;
            _aim.style.left = Length.Percent(viewport.x * 100f);
            _aim.style.top = Length.Percent((1f - viewport.y) * 100f);
        }
    }
}
