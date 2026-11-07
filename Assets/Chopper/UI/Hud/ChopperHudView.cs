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
    /// The view models come from the scene's <see cref="PresentationBridge"/>. The tree comes from the <see cref="PanelRenderer"/>
    /// through its reload callback, so the view rebinds when the panel is rebuilt (HUD look, UXML hot reload).
    /// </summary>
    [RequireComponent(typeof(PanelRenderer))]
    public sealed class ChopperHudView : MonoBehaviour
    {
        [SerializeField, Tooltip("Scene presentation bridge; it owns the HUD view models.")]
        private PresentationBridge bridge;

        private const string HiddenClass = "hud--hidden";
        private const string BoostReadyClass = "hud-boost--ready";
        /// <summary>On the chopper layer while boosting: USS drives the HUD's boost look from it.</summary>
        private const string BoostingClass = "hud--boosting";
        private const string HealthLowClass = "hud-readout--low";
        private const string ArcLowClass = "hud-arc--low";
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
        private const string PylonAmmoEmptyClass = "hud-pylon-ammo--empty";
        private const string ReloadHiddenClass = "hud-reload--hidden";
        private const string HeatNoneClass = "hud-heat--none";
        private const string HeatLabelHotClass = "hud-heat-label--hot";
        private const string AbilityHiddenClass = "hud-ability--hidden";
        private const string AbilityReadyClass = "hud-ability--ready";
        private const string AbilityActiveClass = "hud-ability--active";
        private const string RepairHiddenClass = "hud-repair--hidden";
        private const string RepairActiveClass = "hud-repair--active";
        private const string RepairLockedClass = "hud-repair--locked";
        private const string HullRepairingClass = "hud-readout--repairing";
        private const string ArcRepairingClass = "hud-arc--repairing";
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
        private VisualElement _pylonAmmo;
        private VisualElement _flares;
        private VisualElement _heatArc;
        private Label _heatLabel;
        private VisualElement _pylonReload;
        private VisualElement _pylonReloadFill;
        private VisualElement _flareReload;
        private VisualElement _flareReloadFill;
        private VisualElement _horizon;
        private VisualElement _banner;
        private VisualElement _boost;
        private VisualElement _health;
        private VisualElement _hullArc;
        private VisualElement _repair;
        private VisualElement _repairBar;
        private VisualElement _ability;
        private VisualElement _jamWarning;
        private VisualElement _aim;
        private PanelRenderer _renderer;
        private WorldMarkerView _markers;
        private ScoreboardView _scoreboard;
        private bool _subscribed;

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

            // First: the look may move the panel into a render texture, which rebuilds the tree.
            _renderer = GetComponent<PanelRenderer>();
            bridge.Context.HudLook.Attach(_renderer);
            // Called at once if the tree exists, and again after each rebuild (panel change, UXML hot reload).
            _renderer.RegisterUIReloadCallback(OnUIReload);
        }

        private void OnDisable()
        {
            if (_renderer != null) _renderer.UnregisterUIReloadCallback(OnUIReload);
            Unbind();
            if (bridge != null && _renderer != null && !bridge.Context.IsDisposed)
                bridge.Context.HudLook.Detach(_renderer);
        }

        private void OnUIReload(PanelRenderer panelRenderer, VisualElement rootElement, int version)
        {
            Unbind();
            var root = rootElement?.Q("hud");
            if (root == null) return;
            BindTree(root);
        }

        private void BindTree(VisualElement root)
        {
            _chopper = root.Q("chopper");
            _boost = _chopper.Q("boost");
            _health = _chopper.Q("health");
            _hullArc = _chopper.Q("hull-arc");
            _repair = _chopper.Q("repair");
            _repairBar = _chopper.Q("repair-bar");
            _ability = _chopper.Q("ability");
            _jamWarning = _chopper.Q("jam-warning");
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
            _pylonAmmo = _chopper.Q("pylon-ammo");
            _flares = _chopper.Q("flares");
            _heatArc = _chopper.Q("heat-arc");
            _heatLabel = _chopper.Q<Label>("heat-label");
            _pylonReload = _chopper.Q("pylon-reload");
            _pylonReloadFill = _chopper.Q("pylon-reload-fill");
            _flareReload = _chopper.Q("flare-reload");
            _flareReloadFill = _chopper.Q("flare-reload-fill");
            Bind(_chopper.Q<Label>("pylon"), "text", nameof(ChopperHudModel.PylonText));
            // Plain text: fixed-width digits squeeze the letters of RELOAD and FLARES at this size.
            Bind(_chopper.Q<Label>("pylon-ammo"), "text", nameof(ChopperHudModel.PylonAmmoText));
            Bind(_chopper.Q<Label>("flares"), "text", nameof(ChopperHudModel.FlaresText));
            Bind(_chopper.Q<ArcGauge>("heat-arc"), nameof(ArcGauge.value), nameof(ChopperHudModel.GunHeat));
            _horizon = _chopper.Q("horizon");
            _markers = new WorldMarkerView(bridge.Context.Markers, _chopper.Q("markers"), _chopper.Q("lead"));

            _chopper.dataSource = _model;
            BindNumber(_chopper.Q<Label>("health-value"), nameof(ChopperHudModel.HealthText));
            BindNumber(_chopper.Q<Label>("repair"), nameof(ChopperHudModel.RepairText));
            BindNumber(_chopper.Q<Label>("ability"), nameof(ChopperHudModel.AbilityText));
            BindNumber(_chopper.Q<Label>("speed-value"), nameof(ChopperHudModel.SpeedText));
            BindNumber(_chopper.Q<Label>("altitude-value"), nameof(ChopperHudModel.AltitudeText));
            Bind(_chopper.Q<ArcGauge>("boost"), nameof(ArcGauge.value), nameof(ChopperHudModel.BoostEnergy));
            Bind(_chopper.Q<ArcGauge>("hull-arc"), nameof(ArcGauge.value), nameof(ChopperHudModel.HealthFraction));

            var match = root.Q("match");
            _banner = match.Q("banner");
            match.dataSource = _match;
            BindNumber(match.Q<Label>("timer"), nameof(MatchHudModel.TimerText));
            Bind(match.Q<Label>("local-score"), "text", nameof(MatchHudModel.LocalScoreText));
            Bind(match.Q<Label>("local-pilot"), "text", nameof(MatchHudModel.LocalPilotText));
            Bind(match.Q<Label>("leader"), "text", nameof(MatchHudModel.LeaderText));
            Bind(match.Q<Label>("kill-feed"), "text", nameof(MatchHudModel.KillFeedText));
            Bind(_banner, "text", nameof(MatchHudModel.BannerText));
            _scoreboard = new ScoreboardView(match, _match);

            _model.propertyChanged += OnModelChanged;
            _match.propertyChanged += OnMatchChanged;
            _subscribed = true;
            RefreshClasses();
            RefreshAim();
            RefreshDamageDirection();
            RefreshSway();
            RefreshHorizon();
            RefreshLock();
            RefreshThreats();
            RefreshWeapons();
            RefreshReload();
            RefreshRepair();
            RefreshAbility();
            RefreshBanner();
        }

        /// <summary>Drops the old tree's elements and model events (before a rebuild, or on disable).</summary>
        private void Unbind()
        {
            _markers?.Dispose();
            _markers = null;
            _scoreboard?.Dispose();
            _scoreboard = null;
            if (_subscribed)
            {
                _model.propertyChanged -= OnModelChanged;
                _match.propertyChanged -= OnMatchChanged;
                _subscribed = false;
            }
            _chopper = null;
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

        /// <summary>Text binding with fixed-width digits (<see cref="TabularText"/>), so changing numbers do not move the HUD.</summary>
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

        private void OnModelChanged(object sender, BindablePropertyChangedEventArgs args)
        {
            // Also when the panel dropped the tree (rebuild) before the reload callback rebinds: no styles on dead elements.
            if (_chopper == null || _chopper.panel == null) return;
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
                     args.propertyName == nameof(ChopperHudModel.FlaresReady) ||
                     args.propertyName == nameof(ChopperHudModel.PylonEmpty) ||
                     args.propertyName == nameof(ChopperHudModel.GunHasHeat) ||
                     args.propertyName == nameof(ChopperHudModel.GunOverheated))
                RefreshWeapons();
            else if (args.propertyName == nameof(ChopperHudModel.PylonReload) ||
                     args.propertyName == nameof(ChopperHudModel.FlareReload))
                RefreshReload();
            else if (args.propertyName == nameof(ChopperHudModel.RollDegrees))
                RefreshHorizon();
            else if (args.propertyName == nameof(ChopperHudModel.Repair))
                RefreshRepair();
            else if (args.propertyName == nameof(ChopperHudModel.RepairLockProgress))
                RefreshRepairLock();
            else if (args.propertyName == nameof(ChopperHudModel.Ability) || args.propertyName == nameof(ChopperHudModel.Jammed))
                RefreshAbility();
            else if (args.propertyName == nameof(ChopperHudModel.DamageDirectionDegrees))
                RefreshDamageDirection();
            else if (args.propertyName == nameof(ChopperHudModel.HasChopper) ||
                     args.propertyName == nameof(ChopperHudModel.BoostReady) ||
                     args.propertyName == nameof(ChopperHudModel.Boosting) ||
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
            _boost.EnableInClassList(BoostReadyClass, _model.BoostReady);
            _chopper.EnableInClassList(BoostingClass, _model.Boosting);
            _health.EnableInClassList(HealthLowClass, _model.HealthLow);
            _hullArc.EnableInClassList(ArcLowClass, _model.HealthLow);
            _aim.EnableInClassList(AimHiddenClass, !_model.AimVisible);
            _aim.EnableInClassList(HitClass, _model.HitMarkerVisible);
            _aim.EnableInClassList(KillClass, _model.KillMarkerVisible);
        }

        private void RefreshAbility()
        {
            if (_ability == null) return;
            var state = _model.Ability;
            _ability.EnableInClassList(AbilityHiddenClass, state == HudAbility.Hidden);
            _ability.EnableInClassList(AbilityReadyClass, state == HudAbility.Ready);
            _ability.EnableInClassList(AbilityActiveClass, state == HudAbility.Active);
            _jamWarning?.EnableInClassList(ThreatHiddenClass, !_model.Jammed);
        }

        private void RefreshRepair()
        {
            if (_repair == null) return;
            var state = _model.Repair;
            _repair.EnableInClassList(RepairHiddenClass, state == HudRepair.Hidden);
            _repair.EnableInClassList(RepairActiveClass, state == HudRepair.Active);
            _repair.EnableInClassList(RepairLockedClass, state == HudRepair.Locked);
            _health.EnableInClassList(HullRepairingClass, state == HudRepair.Active);
            _hullArc.EnableInClassList(ArcRepairingClass, state == HudRepair.Active);
            RefreshRepairLock();
        }

        private void RefreshRepairLock()
        {
            if (_repairBar != null) _repairBar.style.width = Length.Percent(_model.RepairLockProgress * 100f);
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
            _pylonAmmo.EnableInClassList(PylonAmmoEmptyClass, _model.PylonEmpty);
            _heatArc.EnableInClassList(HeatNoneClass, !_model.GunHasHeat);
            _heatArc.EnableInClassList(ArcLowClass, _model.GunOverheated);
            _heatLabel.EnableInClassList(HeatNoneClass, !_model.GunHasHeat);
            _heatLabel.EnableInClassList(HeatLabelHotClass, _model.GunOverheated);
            _heatLabel.text = _model.GunOverheated ? "OVERHEAT" : "GUN";
        }

        private void RefreshReload()
        {
            SetReload(_pylonReload, _pylonReloadFill, _model.PylonReload);
            SetReload(_flareReload, _flareReloadFill, _model.FlareReload);
        }

        /// <summary>A full magazine (1) shows no bar.</summary>
        private static void SetReload(VisualElement bar, VisualElement fill, float progress)
        {
            bar.EnableInClassList(ReloadHiddenClass, progress >= 1f);
            fill.style.width = Length.Percent(progress * 100f);
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
            // Also when the panel dropped the tree (rebuild) before the reload callback rebinds: no styles on dead elements.
            if (_chopper == null || _chopper.panel == null) return;
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
