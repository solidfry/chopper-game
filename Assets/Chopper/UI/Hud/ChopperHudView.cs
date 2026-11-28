using System.Collections.Generic;
using Chopper.Presentation.Bridge;
using Chopper.Presentation.Hud;
using Chopper.UI.Cockpit;
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
        /// <summary>One short yellow pulse of the reticle per confirmed hit (same size each time, never stacked).</summary>
        private const string PulseClass = "hud-aim--pulse";
        private const long PulseMilliseconds = 110;
        private IVisualElementScheduledItem _pulseOff;
        private const string KillClass = "hud-aim--kill";
        private const string WarningHiddenClass = "hud-warning--hidden";
        private const string BannerHiddenClass = "hud-match__banner--hidden";
        private const string DamageHiddenClass = "hud-damage--hidden";
        private const string LockHiddenClass = "hud-lock--hidden";
        private const string LockedClass = "hud-lock--locked";
        private const string LockLowClass = "hud-lock--low";
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
        private Label _lockLabel;
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
        private VisualElement _matchScores;
        private VisualElement _killFeed;
        private VisualElement _deathTint;
        private VisualElement _deathAlert;
        private TerminalReveal _deathReveal;
        private TerminalReveal _bannerReveal;
        private TerminalReveal _warningReveal;
        private bool _wasOutOfBounds;
        private string _warningText = "";
        private VisualElement _xpPopup;
        private VisualElement _objective;
        private Label _objectiveText;
        private VisualElement _objectiveBar;
        private VisualElement _objectiveFill;
        private Label _nukeTimer;
        private VisualElement _pilot;
        private VisualElement _pilotPicture;
        private const string PilotHiddenClass = "hud-pilot--hidden";
        private readonly List<(HudGroup Group, VisualElement Element)> _opacityGroups = new();
        private Vector2 _panelInset;
        private bool _subscribed;

        /// <summary>Corner text stays this far (part of the panel) inside the screen corners: about 32 px at 1080p.</summary>
        private static readonly Vector2 CornerMargin = new(0.017f, 0.03f);

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

        /// <summary>Optics line (zoom, special view, or the key hint). Null in a layout without it.</summary>
        private Label _zoom;

        private void BindTree(VisualElement root)
        {
            _chopper = root.Q("chopper");
            _boost = _chopper.Q("boost");
            _health = _chopper.Q("health");
            _hullArc = _chopper.Q("hull-arc");
            _repair = _chopper.Q("repair-line");
            _repairBar = _chopper.Q("repair-bar");
            _ability = _chopper.Q("ability");
            _jamWarning = _chopper.Q("jam-warning");
            _aim = _chopper.Q("aim");
            _boundsWarning = _chopper.Q("bounds-warning");
            var warningText = _boundsWarning?.Q<Label>();
            _warningReveal = new TerminalReveal(warningText, warningText, "#FF4246");
            _warningText = warningText?.text ?? "";
            _wasOutOfBounds = false;
            _damageRing = _chopper.Q("damage-ring");
            _frame = _chopper.Q("frame");
            _lock = _chopper.Q("lock");
            _lockLabel = _chopper.Q<Label>("lock-label");
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
            // Empty text = no zoom: the label takes no room then.
            _zoom = _chopper.Q<Label>("zoom");
            if (_zoom != null) Bind(_zoom, "text", nameof(ChopperHudModel.ZoomText));
            // Plain text: fixed-width digits squeeze the letters of RELOAD and FLARES at this size.
            Bind(_chopper.Q<Label>("pylon-ammo"), "text", nameof(ChopperHudModel.PylonAmmoText));
            Bind(_chopper.Q<Label>("flares"), "text", nameof(ChopperHudModel.FlaresText));
            Bind(_chopper.Q<ArcGauge>("heat-arc"), nameof(ArcGauge.value), nameof(ChopperHudModel.GunHeat));
            var attitude = _chopper.Q<AttitudeIndicator>("attitude");
            Bind(attitude, nameof(AttitudeIndicator.roll), nameof(ChopperHudModel.RollDegrees));
            Bind(attitude, nameof(AttitudeIndicator.pitch), nameof(ChopperHudModel.PitchDegrees));
            _markers = new WorldMarkerView(bridge.Context.Markers, bridge.Context.HudLook, _chopper.Q("markers"), _chopper.Q("lead"),
                _chopper.Q("gun-lead"));

            _chopper.dataSource = _model;
            BindNumber(_chopper.Q<Label>("health-value"), nameof(ChopperHudModel.HealthText));
            BindLastNumber(_chopper.Q<Label>("repair"), nameof(ChopperHudModel.RepairText));
            BindLastNumber(_chopper.Q<Label>("ability"), nameof(ChopperHudModel.AbilityText));
            BindNumber(_chopper.Q<Label>("speed-value"), nameof(ChopperHudModel.SpeedText));
            BindNumber(_chopper.Q<Label>("altitude-value"), nameof(ChopperHudModel.AltitudeText));
            Bind(_chopper.Q<ArcGauge>("boost"), nameof(ArcGauge.value), nameof(ChopperHudModel.BoostEnergy));
            Bind(_chopper.Q<ArcGauge>("hull-arc"), nameof(ArcGauge.value), nameof(ChopperHudModel.HealthFraction));

            var match = root.Q("match");
            _matchScores = match.Q("match-scores");
            _killFeed = match.Q("kill-feed");
            _panelInset = Vector2.zero;
            _banner = match.Q("banner");
            match.dataSource = _match;
            BindNumber(match.Q<Label>("timer"), nameof(MatchHudModel.TimerText));
            BindNumber(match.Q<Label>("local-kills"), nameof(MatchHudModel.LocalKillsText));
            BindNumber(match.Q<Label>("local-deaths"), nameof(MatchHudModel.LocalDeathsText));
            // The call sign sits under the pilot portrait (flight layer), but reads the match model.
            var localPilot = _chopper.Q<Label>("local-pilot");
            if (localPilot != null)
            {
                localPilot.dataSource = _match;
                Bind(localPilot, "text", nameof(MatchHudModel.LocalPilotText));
            }
            Bind(match.Q<Label>("leader"), "text", nameof(MatchHudModel.LeaderText));
            Bind(match.Q<Label>("kill-feed"), "text", nameof(MatchHudModel.KillFeedText));
            _bannerReveal = new TerminalReveal(_banner, _banner as Label, "#00FF94");
            _deathTint = match.Q("death-tint");
            _deathAlert = match.Q("death-alert");
            var deathText = match.Q<Label>("death-alert-text");
            _deathReveal = new TerminalReveal(deathText, deathText, "#FF4246");
            Bind(match.Q<Label>("death-alert-respawn"), "text", nameof(MatchHudModel.RespawnText));
            _objective = match.Q("objective");
            _objectiveText = match.Q<Label>("objective-text");
            _objectiveBar = match.Q("objective-bar");
            _objectiveFill = match.Q("objective-fill");
            _nukeTimer = match.Q<Label>("nuke-timer");
            _xpPopup = match.Q("xp-popup");
            Bind(match.Q<Label>("xp-title"), "text", nameof(MatchHudModel.XpTitle));
            Bind(match.Q<Label>("xp-name"), "text", nameof(MatchHudModel.XpName));
            Bind(match.Q<Label>("xp-total"), "text", nameof(MatchHudModel.XpTotalText));
            Bind(match.Q<Label>("xp-lines"), "text", nameof(MatchHudModel.XpLinesText));
            _scoreboard = new ScoreboardView(match, _match);

            _opacityGroups.Clear();
            AddToGroup(HudGroup.Horizon, attitude);
            AddToGroup(HudGroup.FlightRing, _chopper.Q("ring"));
            AddToGroup(HudGroup.Readouts, _chopper.Q("readouts"), _health);
            AddToGroup(HudGroup.Weapons, _chopper.Q("left"));
            AddToGroup(HudGroup.Targeting, _chopper.Q("aim-layer"), _chopper.Q("markers"));
            AddToGroup(HudGroup.MatchInfo, _matchScores, _killFeed, match.Q("timer"));
            _pilot = _chopper.Q("pilot");
            _pilotPicture = _pilot?.Q("pilot-picture");
            if (_pilot != null) AddToGroup(HudGroup.Pilot, _pilot);
            _panelInset = new Vector2(-1f, -1f);

            _model.propertyChanged += OnModelChanged;
            _match.propertyChanged += OnMatchChanged;
            bridge.Context.HudOpacity.Changed += RefreshOpacity;
            _subscribed = true;
            RefreshOpacity();
            RefreshClasses();
            RefreshAim();
            RefreshReticle();
            RefreshDamageDirection();
            RefreshSway();
            RefreshLock();
            RefreshThreats();
            RefreshWeapons();
            RefreshReload();
            RefreshRepair();
            RefreshAbility();
            RefreshBanner();
            RefreshDeathAlert();
            RefreshObjective();
            RefreshNukeTimer();
            RefreshXp();
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
                bridge.Context.HudOpacity.Changed -= RefreshOpacity;
                _subscribed = false;
            }
            _chopper = null;
        }

        private void AddToGroup(HudGroup group, params VisualElement[] elements)
        {
            foreach (var element in elements) _opacityGroups.Add((group, element));
        }

        private void RefreshOpacity()
        {
            var opacity = bridge.Context.HudOpacity;
            foreach (var (group, element) in _opacityGroups) element.style.opacity = opacity.Get(group);
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

        /// <summary>Words with a number at the end: only the number has fixed-width digits, so the words keep their letter-spacing.</summary>
        private static void BindLastNumber(Label label, string modelProperty)
        {
            var binding = new DataBinding
            {
                dataSourcePath = new PropertyPath(modelProperty),
                bindingMode = BindingMode.ToTarget,
            };
            binding.sourceToUiConverters.AddConverter((ref string value) => TabularText.WrapLastNumber(value));
            label.SetBinding("text", binding);
        }

        /// <summary>
        /// Yellow and a little bigger for a moment. A hit during a pulse restarts it (off for one frame, then on), so
        /// a fast gun gives a pulse per hit and the size never grows past the one step.
        /// </summary>
        private void PulseReticle()
        {
            if (_aim == null) return;
            _pulseOff?.Pause();
            if (_aim.ClassListContains(PulseClass))
            {
                _aim.RemoveFromClassList(PulseClass);
                _aim.schedule.Execute(() => _aim.AddToClassList(PulseClass)).StartingIn(16);
            }
            else
                _aim.AddToClassList(PulseClass);
            _pulseOff = _aim.schedule.Execute(() => _aim.RemoveFromClassList(PulseClass)).StartingIn(PulseMilliseconds);
        }

        /// <summary>One reticle shape class on the aim element for the selected weapon type (<see cref="Reticles"/>).</summary>
        private void RefreshReticle()
        {
            if (_aim == null) return;
            for (var kind = ReticleKind.Gun; kind <= ReticleKind.Drone; kind++)
            {
                var name = Reticles.ClassOf(kind);
                if (name.Length > 0) _aim.EnableInClassList(name, kind == _model.Reticle);
            }
        }

        private void OnModelChanged(object sender, BindablePropertyChangedEventArgs args)
        {
            // Also when the panel dropped the tree (rebuild) before the reload callback rebinds: no styles on dead elements.
            if (_chopper == null || _chopper.panel == null) return;
            // Respawned. This event fires only on a change, so a kill that arrives just before the chopper goes stays shown.
            if (args.propertyName == nameof(ChopperHudModel.HasChopper) && _model.HasChopper) _match.ClearDeathAlert();
            if (args.propertyName == nameof(ChopperHudModel.ZoomIsHint))
                _zoom?.EnableInClassList("hud-zoom--hint", _model.ZoomIsHint);
            else if (args.propertyName == nameof(ChopperHudModel.AimViewport))
                RefreshAim();
            else if (args.propertyName == nameof(ChopperHudModel.Reticle))
                RefreshReticle();
            else if (args.propertyName == nameof(ChopperHudModel.HitCount))
                PulseReticle();
            else if (args.propertyName == nameof(ChopperHudModel.Sway))
                RefreshSway();
            else if (args.propertyName == nameof(ChopperHudModel.LockViewport) ||
                     args.propertyName == nameof(ChopperHudModel.LockProgress) ||
                     args.propertyName == nameof(ChopperHudModel.LockVisible) ||
                     args.propertyName == nameof(ChopperHudModel.Locked) ||
                     args.propertyName == nameof(ChopperHudModel.LockTooLow))
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
            _zoom?.EnableInClassList("hud-zoom--hint", _model.ZoomIsHint);
            _boundsWarning.EnableInClassList(WarningHiddenClass, !_model.OutOfBounds);
            if (_model.OutOfBounds != _wasOutOfBounds)
            {
                _wasOutOfBounds = _model.OutOfBounds;
                if (_wasOutOfBounds) _warningReveal?.Play(_warningText);
                else _warningReveal?.Stop();
            }
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
            _lock.EnableInClassList(LockLowClass, _model.LockTooLow);
            if (_lockLabel != null) _lockLabel.text = _model.LockTooLow ? "TOO LOW" : "LOCK";
            if (!_model.LockVisible) return;

            var viewport = bridge.Context.HudLook.ToHud(_model.LockViewport);
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

        private void RefreshDamageDirection()
        {
            _damageRing.style.rotate = new Rotate(Angle.Degrees(_model.DamageDirectionDegrees));
        }

        private void OnMatchChanged(object sender, BindablePropertyChangedEventArgs args)
        {
            // Also when the panel dropped the tree (rebuild) before the reload callback rebinds: no styles on dead elements.
            if (_chopper == null || _chopper.panel == null) return;
            if (args.propertyName == nameof(MatchHudModel.BannerVisible) || args.propertyName == nameof(MatchHudModel.BannerText))
                RefreshBanner();
            else if (args.propertyName == nameof(MatchHudModel.DeathAlertVisible) || args.propertyName == nameof(MatchHudModel.DeathAlertText))
                RefreshDeathAlert();
            else if (args.propertyName == nameof(MatchHudModel.XpVisible) || args.propertyName == nameof(MatchHudModel.XpBig))
                RefreshXp();
            else if (args.propertyName == nameof(MatchHudModel.XpPulse))
                PunchXp();
            else if (args.propertyName == nameof(MatchHudModel.ObjectiveText) || args.propertyName == nameof(MatchHudModel.ObjectiveProgress) ||
                     args.propertyName == nameof(MatchHudModel.ObjectiveTone))
                RefreshObjective(args.propertyName == nameof(MatchHudModel.ObjectiveText));
            else if (args.propertyName == nameof(MatchHudModel.NukeTimerText) || args.propertyName == nameof(MatchHudModel.NukeTimerTone) ||
                     args.propertyName == nameof(MatchHudModel.NukeTimerFinal))
                RefreshNukeTimer();
        }

        /// <summary>The nuke countdown pill at the top (every player sees it).</summary>
        private void RefreshNukeTimer()
        {
            if (_nukeTimer == null) return;
            var text = _match.NukeTimerText;
            _nukeTimer.text = text;
            _nukeTimer.EnableInClassList("hud-nuke-timer--hidden", string.IsNullOrEmpty(text));
            _nukeTimer.EnableInClassList("hud-nuke-timer--good", _match.NukeTimerTone == CalloutTone.Good);
            _nukeTimer.EnableInClassList("hud-nuke-timer--alert", _match.NukeTimerTone == CalloutTone.Alert);
            _nukeTimer.EnableInClassList("hud-nuke-timer--final", _match.NukeTimerFinal);
        }

        private void RefreshXp()
        {
            if (_xpPopup == null) return;
            _xpPopup.EnableInClassList("hud-xp--hidden", !_match.XpVisible);
            _xpPopup.EnableInClassList("hud-xp--big", _match.XpBig);
        }

        /// <summary>Each award slams the popup in big for one frame; the transition settles it back with an overshoot.</summary>
        private void PunchXp()
        {
            if (_xpPopup == null) return;
            RefreshXp();
            _xpPopup.AddToClassList("hud-xp--punch");
            _xpPopup.schedule.Execute(() => _xpPopup?.RemoveFromClassList("hud-xp--punch")).StartingIn(30);
        }

        /// <summary>Capture the Nuke line: text, tone colour, bar. A flash (hooked, armed ...) punches in bigger.</summary>
        private void RefreshObjective(bool newText = false)
        {
            if (_objective == null) return;
            var text = _match.ObjectiveText;
            _objective.EnableInClassList("hud-objective--hidden", string.IsNullOrEmpty(text));
            _objective.EnableInClassList("hud-objective--good", _match.ObjectiveTone == CalloutTone.Good);
            _objective.EnableInClassList("hud-objective--alert", _match.ObjectiveTone == CalloutTone.Alert);
            if (_objectiveText != null) _objectiveText.text = text;
            var progress = _match.ObjectiveProgress;
            _objectiveBar?.EnableInClassList("hud-objective__bar--hidden", progress < 0f);
            if (_objectiveFill != null) _objectiveFill.style.width = Length.Percent(Mathf.Clamp01(progress) * 100f);
            if (newText) _objective.EnableInClassList("hud-objective--flash", _match.ObjectiveFlashing);
        }

        private void RefreshBanner()
        {
            _banner.EnableInClassList(BannerHiddenClass, !_match.BannerVisible);
            Reveal(_bannerReveal, _match.BannerVisible, _match.BannerText);
        }

        private void RefreshDeathAlert()
        {
            _deathTint.EnableInClassList("hud-death-tint--hidden", !_match.DeathAlertVisible);
            _deathAlert.EnableInClassList("hud-death-alert--hidden", !_match.DeathAlertVisible);
            Reveal(_deathReveal, _match.DeathAlertVisible, _match.DeathAlertText);
        }

        /// <summary>Types the pill in when it shows or its text changes; stops when it hides.</summary>
        private static void Reveal(TerminalReveal reveal, bool visible, string text)
        {
            if (reveal == null) return;
            if (!visible) reveal.Stop();
            else if (!reveal.Playing || reveal.Source != (text ?? "")) reveal.Play(text);
        }

        /// <summary>Viewport y goes up, panel top goes down.</summary>
        private void RefreshAim()
        {
            var viewport = bridge.Context.HudLook.ToHud(_model.AimViewport);
            _aim.style.left = Length.Percent(viewport.x * 100f);
            _aim.style.top = Length.Percent((1f - viewport.y) * 100f);
        }

        /// <summary>
        /// The sphere look cuts off the panel's corners: the corner text moves in by that much, for any bend and window shape.
        /// Each frame, so tuning the angle in the inspector and changing the window work at once. Writes only on a change.
        /// </summary>
        /// <summary>The pilot portrait shows while the rig has a pilot with art (its texture is drawn by <see cref="LocalPilotPortraitSystem"/>).</summary>
        private void RefreshPilot()
        {
            if (_pilot == null) return;
            var rig = bridge.Context.PilotPortrait;
            var show = rig.HasPilot;
            _pilot.EnableInClassList(PilotHiddenClass, !show);
            if (!show || _pilotPicture == null) return;
            var texture = _pilotPicture.style.backgroundImage.value.renderTexture;
            if (texture != rig.Texture) _pilotPicture.style.backgroundImage = Background.FromRenderTexture(rig.Texture);
        }

        private void Update()
        {
            if (_matchScores == null || _matchScores.panel == null) return;
            RefreshPilot();
            var look = bridge.Context.HudLook;
            var inset = look.PanelInset;
            if (inset == _panelInset) return;
            _panelInset = inset;

            var sideways = inset.x > 0f ? Length.Percent((inset.x + CornerMargin.x) * 100f) : new StyleLength(StyleKeyword.Null);
            var down = inset.y > 0f ? Length.Percent((inset.y + CornerMargin.y) * 100f) : new StyleLength(StyleKeyword.Null);
            _matchScores.style.left = sideways;
            _matchScores.style.top = down;
            _killFeed.style.right = sideways;
            _killFeed.style.top = down;
            if (_pilot != null) _pilot.style.bottom = down;

            RefreshAim();
            RefreshLock();
        }
    }
}
