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
        private const string LockHiddenClass = "hud-lock--hidden";
        private const string LockedClass = "hud-lock--locked";
        private const string ThreatHiddenClass = "hud-threat--hidden";
        private const string ThreatsDimClass = "hud-threats--dim";
        private const string PylonNeedsLockClass = "hud-pylon--needs-lock";
        private const string FlaresReadyClass = "hud-flares--ready";
        /// <summary>Lock bracket size in pixels: wide when the lock starts, tight when locked.</summary>
        private const float LockStartSize = 140f;
        private const float LockEndSize = 56f;

        private ChopperHudModel _model;
        private MatchHudModel _match;
        private VisualElement _chopper;
        private VisualElement _boundsWarning;
        private VisualElement _damageRing;
        private VisualElement _frame;
        private VisualElement _lock;
        private VisualElement _threats;
        private Label _missileWarning;
        private Label _lockWarning;
        private VisualElement _missileRing;
        private VisualElement _pylon;
        private VisualElement _flares;
        private VisualElement _horizon;
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
            _frame = _chopper.Q("frame");
            _lock = _chopper.Q("lock");
            _threats = _chopper.Q("threats");
            _missileWarning = _chopper.Q<Label>("missile-warning");
            _lockWarning = _chopper.Q<Label>("lock-warning");
            _missileRing = _chopper.Q("missile-ring");
            _pylon = _chopper.Q("pylon");
            _flares = _chopper.Q("flares");
            Bind(_chopper.Q<Label>("pylon"), "text", nameof(ChopperHudModel.PylonText));
            _horizon = _chopper.Q("horizon");

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
            RefreshSway();
            RefreshHorizon();
            RefreshLock();
            RefreshThreats();
            RefreshWeapons();
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
            else if (args.propertyName == nameof(ChopperHudModel.Sway))
                RefreshSway();
            else if (args.propertyName == nameof(ChopperHudModel.LockViewport) ||
                     args.propertyName == nameof(ChopperHudModel.LockProgress) ||
                     args.propertyName == nameof(ChopperHudModel.LockVisible) ||
                     args.propertyName == nameof(ChopperHudModel.Locked))
                RefreshLock();
            else if (args.propertyName == nameof(ChopperHudModel.MissileInbound) ||
                     args.propertyName == nameof(ChopperHudModel.MissileDirectionDegrees) ||
                     args.propertyName == nameof(ChopperHudModel.ThreatLocking) ||
                     args.propertyName == nameof(ChopperHudModel.ThreatLocked) ||
                     args.propertyName == nameof(ChopperHudModel.WarningBlink))
                RefreshThreats();
            else if (args.propertyName == nameof(ChopperHudModel.PylonNeedsLock) ||
                     args.propertyName == nameof(ChopperHudModel.FlaresReady))
                RefreshWeapons();
            else if (args.propertyName == nameof(ChopperHudModel.RollDegrees))
                RefreshHorizon();
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

        private void RefreshLock()
        {
            RefreshWeapons();
            _lock.EnableInClassList(LockHiddenClass, !_model.LockVisible);
            _lock.EnableInClassList(LockedClass, _model.Locked);
            if (!_model.LockVisible) return;

            var viewport = _model.LockViewport;
            _lock.style.left = Length.Percent(viewport.x * 100f);
            _lock.style.top = Length.Percent((1f - viewport.y) * 100f);
            var size = Mathf.Lerp(LockStartSize, LockEndSize, _model.LockProgress);
            _lock.style.width = size;
            _lock.style.height = size;
        }

        private void RefreshThreats()
        {
            var lockThreat = !_model.MissileInbound && (_model.ThreatLocking || _model.ThreatLocked);
            _missileWarning.EnableInClassList(ThreatHiddenClass, !_model.MissileInbound);
            _lockWarning.EnableInClassList(ThreatHiddenClass, !lockThreat);
            _lockWarning.text = _model.ThreatLocked ? "LOCKED ON" : "LOCK WARNING";
            _threats.EnableInClassList(ThreatsDimClass, _model.WarningBlink);

            _missileRing.EnableInClassList(DamageHiddenClass, !_model.MissileInbound);
            _missileRing.style.rotate = new Rotate(Angle.Degrees(_model.MissileDirectionDegrees));
        }

        private void RefreshWeapons()
        {
            _pylon.EnableInClassList(PylonNeedsLockClass, _model.PylonNeedsLock && !_model.Locked);
            _flares.EnableInClassList(FlaresReadyClass, _model.FlaresReady);
        }

        private void RefreshSway()
        {
            var sway = _model.Sway;
            _frame.style.translate = new Translate(sway.x, sway.y);
        }

        /// <summary>UI rotation is clockwise; a left bank turns the horizon clockwise.</summary>
        private void RefreshHorizon() => _horizon.style.rotate = new Rotate(Angle.Degrees(_model.RollDegrees));

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
