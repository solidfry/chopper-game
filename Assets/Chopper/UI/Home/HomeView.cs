using System;
using System.Collections.Generic;
using Chopper.Presentation.Bridge;
using Chopper.Presentation.Hangar;
using Chopper.Presentation.Menu;
using Chopper.UI.Cockpit;
using Chopper.UI.Hangar;
using Chopper.UI.Options;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Chopper.UI.Home
{
    /// <summary>
    /// The home screen (main menu) in <c>Home.uxml</c>, over the animated <see cref="HomeBackdrop"/>.
    /// <list type="bullet">
    /// <item>Opens with a short typed boot log (any key or click skips it), then the menu slides in.</item>
    /// <item>Menu: PLAY and HANGAR open the hangar (loadout and FIND MATCH), TRAINING starts the saved training map,
    /// PILOTS opens the codex, OPTIONS the options overlay, QUIT leaves.</item>
    /// <item>Profile card: your pilot (portrait under a <see cref="CodecScan"/>), level bar; click = codex.</item>
    /// <item>Pilot codex: every pilot, read-only, with SET AS PILOT. Q / E or the shoulder buttons step through it.</item>
    /// </list>
    /// Escape / gamepad B: close options, else back out of the codex. Start: options.
    /// </summary>
    [RequireComponent(typeof(PanelRenderer))]
    public sealed class HomeView : MonoBehaviour
    {
        private const string ReadyClass = "home--ready";
        private const string LiveClass = "home--live";
        private const string DimmedClass = "home--dimmed";
        private const string HiddenClass = "home--hidden";
        private const string GlitchClass = "home-title--glitch";
        private const string ShownRowClass = "codex-row--shown";
        private const string SwitchClass = "codex-feature--switch";
        private const string BadEffectClass = "codex-effect--bad";
        private const float BootCharsPerSecond = 220f;

        private static readonly string[] BootLines =
        {
            "AWN//OS 2.6  (C) AIRWING NITRO CORP",
            "MEM CHECK ........ 65536K OK",
            "ROTOR BUS ........ ONLINE",
            "SEEKER CAL ....... NOMINAL",
            "UPLINK ........... LOCAL",
            "OPERATOR AUTH .... ACCEPTED",
        };

        [SerializeField, Tooltip("Scene presentation bridge; it owns the home model.")]
        private PresentationBridge bridge;
        [SerializeField, Tooltip("The options overlay in this scene (OPTIONS). Its own Escape toggle should be off.")]
        private OptionsMenuView options;

        private PresentationContext _context;
        private HomeModel _model;
        private PanelRenderer _renderer;
        private VisualElement _root;
        private InputPromptLabels _prompts;

        private VisualElement _pageMain;
        private VisualElement _pagePilots;
        private VisualElement _title;
        private Label _boot;
        private Label _hint;
        private Label _clock;
        private readonly List<(Button Button, Action Action, Func<string> Hint)> _nav = new();

        private float _portraitTick;
        private float _portraitGlitch;
        private VisualElement _city;
        private Texture _shownCity;
        /// <summary>Frosted glass layers (blurred city cut to their element), placed each frame.</summary>
        private readonly List<VisualElement> _glass = new();

        private Button _profile;
        private VisualElement _profilePortrait;
        private PilotPortrait _profileDrawn;
        private CodecScan _profileScan;
        private Label _profileName;
        private Label _profileSign;
        private Label _profileRole;
        private Label _profileLevel;
        private SegmentBar _profileXp;

        private ScrollView _codexList;
        private Label _codexCount;
        private VisualElement _feature;
        private VisualElement _codexPortrait;
        private PilotPortrait _codexDrawn;
        private CodecScan _codexScan;
        private Label _codexSerial;
        private Label _codexRole;
        private Label _codexSign;
        private Label _codexPassive;
        private Label _codexAbilityName;
        private Label _codexAbilityText;
        private Label _codexAbilityMeta;
        private VisualElement _codexEffects;
        private Label _codexState;
        private Button _codexSelect;
        private Button _codexBack;
        private Button _codexPrev;
        private Button _codexNext;
        private readonly List<Button> _rows = new();

        private string _bootText = "";
        private float _bootTime;
        private bool _ready;
        private float _nextGlitch;
        private float _glitchEnd;
        private int _shownSecond = -1;

        /// <summary>Editor only: used by the setup tool.</summary>
        public void Configure(PresentationBridge value, OptionsMenuView overlay)
        {
            bridge = value;
            options = overlay;
        }

        private void OnEnable()
        {
            if (bridge == null)
            {
                Debug.LogError($"{name}: no presentation bridge set. Run Chopper → Setup, step 11.", this);
                enabled = false;
                return;
            }
            _context = bridge.Context;
            _model = _context.Home;
            if (_model == null)
            {
                Debug.LogError($"{name}: the game config has no loadout catalog. Run Chopper → Setup.", this);
                enabled = false;
                return;
            }
            _model.Changed += Refresh;
            _model.ShotCut += OnShotCut;
            _model.ShownChanged += ShowPilot;
            _bootText = string.Join("\n", BootLines);
            _renderer = GetComponent<PanelRenderer>();
            _renderer.RegisterUIReloadCallback(OnUIReload);
        }

        private void OnDisable()
        {
            if (_model != null)
            {
                _model.Changed -= Refresh;
                _model.ShotCut -= OnShotCut;
                _model.ShownChanged -= ShowPilot;
            }
            if (_renderer != null) _renderer.UnregisterUIReloadCallback(OnUIReload);
            Unbind();
        }

        private void OnUIReload(PanelRenderer panelRenderer, VisualElement rootElement, int version)
        {
            Unbind();
            _root = rootElement?.Q("home");
            if (_root == null) return;
            _prompts = new InputPromptLabels(_root, _context.Prompts);

            _pageMain = _root.Q("page-main");
            _pagePilots = _root.Q("page-pilots");
            _title = _root.Q("title");
            _boot = _root.Q<Label>("boot-log");
            _hint = _root.Q<Label>("nav-hint");
            _clock = _root.Q<Label>("clock");
            _city = _root.Q("city");
            _shownCity = null;

            AddNav("nav-play", OpenHangar, () => "DEPLOY: BUILD YOUR CHOPPER, THEN FIND A MATCH");
            AddNav("nav-training", StartTraining, () =>
                _model.HasTrainingMap ? $"FLY SOLO // {_model.TrainingMapName}" : "NO TRAINING MAP SET UP");
            AddNav("nav-hangar", OpenHangar, () => "CHASSIS, MODULES, SKINS AND PILOT");
            AddNav("nav-pilots", () => _model.ShowPage(HomePage.Pilots), () => "PILOT CODEX: ROLES, PASSIVES, ABILITIES");
            AddNav("nav-options", OpenOptions, () => "CONTROLS, AUDIO, HUD LOOK");
            AddNav("nav-quit", Quit, () => "CLOSE THE TERMINAL");

            _profile = _root.Q<Button>("profile");
            _profilePortrait = _root.Q("profile-portrait");
            _profileDrawn = _root.Q<PilotPortrait>("profile-drawn");
            _profileScan = _root.Q<CodecScan>("profile-scan");
            _profileName = _root.Q<Label>("profile-name");
            _profileSign = _root.Q<Label>("profile-sign");
            _profileRole = _root.Q<Label>("profile-role");
            _profileLevel = _root.Q<Label>("profile-level");
            _profileXp = _root.Q<SegmentBar>("profile-xp");
            if (_profile != null) _profile.clicked += OpenCodex;

            foreach (var (button, _, _) in _nav) AddGlass(button);
            if (_profile != null) AddGlass(_profile);

            _codexList = _root.Q<ScrollView>("codex-list");
            _codexCount = _root.Q<Label>("codex-count");
            _feature = _root.Q("codex-feature");
            _codexPortrait = _root.Q("codex-portrait");
            _codexDrawn = _root.Q<PilotPortrait>("codex-drawn");
            _codexScan = _root.Q<CodecScan>("codex-scan");
            _codexSerial = _root.Q<Label>("codex-serial");
            _codexRole = _root.Q<Label>("codex-role");
            _codexSign = _root.Q<Label>("codex-sign");
            _codexPassive = _root.Q<Label>("codex-passive");
            _codexAbilityName = _root.Q<Label>("codex-ability-name");
            _codexAbilityText = _root.Q<Label>("codex-ability-text");
            _codexAbilityMeta = _root.Q<Label>("codex-ability-meta");
            _codexEffects = _root.Q("codex-effects");
            _codexState = _root.Q<Label>("codex-state");
            _codexSelect = _root.Q<Button>("codex-select");
            _codexBack = _root.Q<Button>("codex-back");
            _codexPrev = _root.Q<Button>("codex-prev");
            _codexNext = _root.Q<Button>("codex-next");
            if (_codexSelect != null) _codexSelect.clicked += SelectPilot;
            if (_codexBack != null) _codexBack.clicked += Back;
            if (_codexPrev != null) _codexPrev.clicked += PreviousPilot;
            if (_codexNext != null) _codexNext.clicked += NextPilot;

            _root.RegisterCallback<PointerDownEvent>(OnAnyPointer, TrickleDown.TrickleDown);

            // The boot log runs once per visit; a rebuilt panel keeps the state.
            if (_ready) FinishBoot();
            else _boot.text = "";
            Refresh();
        }

        private void AddNav(string elementName, Action action, Func<string> hint)
        {
            var button = _root.Q<Button>(elementName);
            if (button == null) return;
            button.clicked += action;
            button.RegisterCallback<PointerEnterEvent>(_ => ShowHint(hint));
            button.RegisterCallback<FocusInEvent>(_ => ShowHint(hint));
            _nav.Add((button, action, hint));
        }

        private void ShowHint(Func<string> hint)
        {
            if (_hint != null) _hint.text = "> " + hint();
        }

        private void Unbind()
        {
            if (_root == null) return;
            _prompts?.Dispose();
            _prompts = null;
            foreach (var (button, action, _) in _nav) button.clicked -= action;
            _nav.Clear();
            if (_profile != null) _profile.clicked -= OpenCodex;
            if (_codexSelect != null) _codexSelect.clicked -= SelectPilot;
            if (_codexBack != null) _codexBack.clicked -= Back;
            if (_codexPrev != null) _codexPrev.clicked -= PreviousPilot;
            if (_codexNext != null) _codexNext.clicked -= NextPilot;
            _root.UnregisterCallback<PointerDownEvent>(OnAnyPointer, TrickleDown.TrickleDown);
            _rows.Clear();
            _glass.Clear();
            _city = null;
            _root = null;
        }

        private void Update()
        {
            if (_root == null) return;
            var time = Time.unscaledTime;
            TickBoot(Time.unscaledDeltaTime);
            TickGlitch(time);
            TickClock();
            PlaceGlass();
            AnimatePortrait();
            var dimmed = _model.Page != HomePage.Main || OptionsOpen;
            _model.SetDimmed(dimmed);
            _root.EnableInClassList(DimmedClass, dimmed);

            var keyboard = Keyboard.current;
            var gamepad = Gamepad.current;
            if (!_ready && ((keyboard != null && keyboard.anyKey.wasPressedThisFrame) ||
                            (gamepad != null && gamepad.buttonSouth.wasPressedThisFrame)))
                FinishBoot();

            var back = (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) ||
                       (gamepad != null && gamepad.buttonEast.wasPressedThisFrame);
            var start = gamepad != null && gamepad.startButton.wasPressedThisFrame;
            if (back) Back();
            else if (start) OpenOptions();

            if (_model.Page != HomePage.Pilots || OptionsOpen) return;
            var previous = (keyboard != null && keyboard.qKey.wasPressedThisFrame) || (gamepad != null && gamepad.leftShoulder.wasPressedThisFrame);
            var next = (keyboard != null && keyboard.eKey.wasPressedThisFrame) || (gamepad != null && gamepad.rightShoulder.wasPressedThisFrame);
            if (previous) PreviousPilot();
            if (next) NextPilot();
        }

        private bool OptionsOpen => options != null && options.IsOpen;

        // ---- Boot, title glitch, clock ----

        private void TickBoot(float deltaTime)
        {
            if (_ready || _boot == null) return;
            _bootTime += deltaTime;
            var count = Mathf.Min(_bootText.Length, Mathf.FloorToInt(_bootTime * BootCharsPerSecond));
            var cursor = Mathf.FloorToInt(_bootTime * 4f) % 2 == 0 ? "_" : " ";
            _boot.text = _bootText.Substring(0, count) + cursor;
            if (count >= _bootText.Length && _bootTime > _bootText.Length / BootCharsPerSecond + 0.25f) FinishBoot();
        }

        private void FinishBoot()
        {
            _ready = true;
            if (_root == null) return;
            if (_boot != null) _boot.text = BootLines[0] + "\n" + BootLines[BootLines.Length - 1];
            if (_root.ClassListContains(ReadyClass)) return;
            _root.AddToClassList(ReadyClass);
            if (_model.Page == HomePage.Main && _nav.Count > 0) _nav[0].Button.Focus();
            Play(_context.Sounds != null ? _context.Sounds.ChassisScan : null);
        }

        private void OnAnyPointer(PointerDownEvent evt)
        {
            if (!_ready) FinishBoot();
        }

        /// <summary>The title splits into pink and cyan ghosts for a moment every few seconds.</summary>
        private void TickGlitch(float time)
        {
            if (_title == null) return;
            if (_glitchEnd > 0f && time >= _glitchEnd)
            {
                _title.RemoveFromClassList(GlitchClass);
                _glitchEnd = 0f;
            }
            if (time < _nextGlitch) return;
            _title.AddToClassList(GlitchClass);
            _glitchEnd = time + UnityEngine.Random.Range(0.06f, 0.16f);
            _nextGlitch = time + UnityEngine.Random.Range(1.8f, 5f);
        }

        /// <summary>The backdrop camera cut: a short CRT glitch over the city hides the jump.</summary>
        private void OnShotCut() => _root?.Q<CodecScan>(className: "home__crt")?.Burst();

        /// <summary>A glass layer under the element's content (first child, not picked).</summary>
        private void AddGlass(VisualElement owner)
        {
            var glass = new VisualElement { pickingMode = PickingMode.Ignore };
            glass.AddToClassList("home-glass");
            glass.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
            owner.Insert(0, glass);
            _glass.Add(glass);
        }

        /// <summary>
        /// The live city image, and each glass layer showing the blurred city at the panel size, shifted so the part
        /// under the element lines up with the city behind it (follows buttons as they slide).
        /// </summary>
        private void PlaceGlass()
        {
            var city = _model.BackdropTexture;
            if (_city != null && city != _shownCity)
            {
                _shownCity = city;
                _city.style.backgroundImage = city is RenderTexture rt ? new StyleBackground(Background.FromRenderTexture(rt)) : new StyleBackground(StyleKeyword.None);
            }

            var blur = _model.BlurTexture as RenderTexture;
            var panel = _root.worldBound;
            foreach (var glass in _glass)
            {
                glass.style.display = blur != null ? DisplayStyle.Flex : DisplayStyle.None;
                if (blur == null) continue;
                glass.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(blur));
                var bound = glass.worldBound;
                glass.style.backgroundSize = new BackgroundSize(new Length(panel.width), new Length(panel.height));
                glass.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Left, new Length(panel.x - bound.x));
                glass.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Top, new Length(panel.y - bound.y));
            }
        }

        private void TickClock()
        {
            if (_clock == null) return;
            var now = DateTime.Now;
            if (now.Second == _shownSecond) return;
            _shownSecond = now.Second;
            _clock.text = TabularText.Wrap(now.ToString("HH:mm:ss"));
        }

        // ---- Pages ----

        private void Refresh()
        {
            if (_root == null) return;
            _root.EnableInClassList(LiveClass, _model.LiveBackdrop);
            var codex = _model.Page == HomePage.Pilots;
            _pageMain?.EnableInClassList(HiddenClass, codex);
            _pagePilots?.EnableInClassList(HiddenClass, !codex);
            ShowProfile();
            if (codex)
            {
                BuildCodex();
                ShowPilot();
            }
        }

        private void ShowProfile()
        {
            var pilot = _model.SelectedPilot;
            if (_profileName != null) _profileName.text = _model.OperatorName;
            if (_profileLevel != null) _profileLevel.text = TabularText.Wrap($"LVL {_model.Level:00}");
            if (_profileXp != null) _profileXp.value = _model.LevelProgress;
            if (pilot == null)
            {
                if (_profileSign != null) _profileSign.text = "NO PILOT";
                return;
            }
            var card = pilot.Value;
            if (_profileSign != null)
            {
                _profileSign.text = card.CallSign;
                _profileSign.style.color = card.Accent;
            }
            if (_profileRole != null) _profileRole.text = card.Role;
            ShowFace(_profilePortrait, _profileDrawn, card);
            if (_profileScan != null) _profileScan.tint = card.Accent;
        }

        /// <summary>
        /// The pilot's live portrait (the shared <see cref="Presentation.Pilots.PilotPortraitRig"/>, in the pilot's accent,
        /// as on the hangar select screen); without the rig the still art tinted in the accent; without art the drawn
        /// helmet. The frame in the pilot's accent. One pilot at a time is live: the codex card, else the profile card.
        /// </summary>
        private void ShowFace(VisualElement frame, PilotPortrait drawn, in PilotCard card)
        {
            if (frame == null) return;
            var rig = _context.PilotPortrait;
            var live = card.Portrait != null && rig != null && rig.Texture != null;
            if (live)
            {
                card.ShowOn(rig);
                rig.UiColour = false;
                frame.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(rig.Texture));
                frame.style.unityBackgroundImageTintColor = Color.white;
            }
            else
            {
                frame.style.backgroundImage = card.Portrait != null ? new StyleBackground(card.Portrait) : new StyleBackground(StyleKeyword.None);
                frame.style.unityBackgroundImageTintColor = card.Accent;
            }
            frame.style.borderTopColor = frame.style.borderBottomColor = frame.style.borderLeftColor = frame.style.borderRightColor = card.Accent;
            if (drawn == null) return;
            drawn.style.display = card.Portrait == null ? DisplayStyle.Flex : DisplayStyle.None;
            drawn.Set(card.Id, card.Accent);
        }

        /// <summary>Animates the live portrait (blink, breath, voice), facing the menu camera, as the hangar does.</summary>
        private void AnimatePortrait()
        {
            var now = Time.unscaledTime;
            var deltaTime = _portraitTick > 0f ? now - _portraitTick : 0f;
            _portraitTick = now;
            var rig = _context.PilotPortrait;
            if (rig == null || !rig.HasPilot) return;
            var camera = UnityEngine.Camera.main;
            var facing = camera != null ? Quaternion.LookRotation(-camera.transform.forward, camera.transform.up) : Quaternion.identity;
            _portraitGlitch = Mathf.MoveTowards(_portraitGlitch, 0f, deltaTime * 3f);
            rig.Tick(deltaTime, new Presentation.Pilots.PilotPortraitInput
            {
                Facing = facing,
                Voice = rig.OwnVoice(_context.Audio),
                Glitch = _portraitGlitch,
            });
        }

        private void BuildCodex()
        {
            if (_codexList == null) return;
            _codexList.Clear();
            _rows.Clear();
            var pilots = _model.Pilots;
            for (var i = 0; i < pilots.Count; i++)
            {
                var index = i;
                var card = pilots[i];
                var row = new Button(() => _model.ShowPilot(index));
                row.AddToClassList("codex-row");
                row.style.borderLeftColor = card.Accent;
                row.RegisterCallback<PointerEnterEvent>(_ => _model.ShowPilot(index));
                row.RegisterCallback<FocusInEvent>(_ => _model.ShowPilot(index));

                var face = new VisualElement { pickingMode = PickingMode.Ignore };
                face.AddToClassList("codex-row__face");
                if (card.Portrait != null)
                {
                    // Still art framed on the face (as the hangar's roster tiles), in the pilot's accent.
                    face.style.backgroundImage = new StyleBackground(card.Portrait);
                    face.style.unityBackgroundImageTintColor = card.Accent;
                    face.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
                    var faceFrame = card.FaceFrame;
                    face.RegisterCallback<GeometryChangedEvent>(_ => PilotSelectView.FrameFace(face, faceFrame));
                }
                else
                {
                    var drawn = new PilotPortrait();
                    drawn.style.flexGrow = 1;
                    drawn.Set(card.Id, card.Accent);
                    face.Add(drawn);
                }
                row.Add(face);
                row.Add(Text(HomeModel.Serial(i), "codex-row__serial", title: false));
                var sign = Text(card.CallSign, "codex-row__sign", title: true);
                row.Add(sign);
                if (card.Id == _model.SelectedPilotId) row.Add(Text("ACTIVE", "codex-row__tag", title: true));
                _codexList.Add(row);
                _rows.Add(row);
            }
            if (_codexCount != null) _codexCount.text = TabularText.Wrap($"{pilots.Count:00} ON FILE");
            var shown = _model.ShownPilot;
            if (shown >= 0 && shown < _rows.Count) _rows[shown].Focus();
        }

        private void ShowPilot()
        {
            var shown = _model.Shown;
            if (shown == null || _root == null) return;
            var card = shown.Value;
            for (var i = 0; i < _rows.Count; i++) _rows[i].EnableInClassList(ShownRowClass, i == _model.ShownPilot);

            ShowFace(_codexPortrait, _codexDrawn, card);
            _portraitGlitch = 1f;
            if (_codexScan != null)
            {
                _codexScan.tint = card.Accent;
                _codexScan.Burst();
            }
            if (_codexSerial != null) _codexSerial.text = TabularText.Wrap($"P-{card.Id:00}");
            if (_codexRole != null) _codexRole.text = card.Role;
            if (_codexSign != null)
            {
                _codexSign.text = card.CallSign;
                _codexSign.style.color = card.Accent;
            }
            if (_codexPassive != null) _codexPassive.text = card.Passive;
            if (_codexAbilityName != null) _codexAbilityName.text = card.AbilityName;
            if (_codexAbilityText != null) _codexAbilityText.text = card.AbilityText;
            if (_codexAbilityMeta != null) _codexAbilityMeta.text = card.AbilityMeta;
            if (_codexEffects != null)
            {
                _codexEffects.Clear();
                foreach (var (text, good) in card.Effects)
                {
                    var chip = Text(text, "codex-effect", title: true);
                    chip.EnableInClassList(BadEffectClass, !good);
                    _codexEffects.Add(chip);
                }
            }

            var active = card.Id == _model.SelectedPilotId;
            if (_codexState != null) _codexState.text = card.Locked ? $"UNLOCKS AT LEVEL {card.UnlockLevel}" : active ? "CURRENT PILOT" : "";
            if (_codexSelect != null)
            {
                _codexSelect.SetEnabled(!card.Locked && !active);
                _codexSelect.text = active ? "[ ACTIVE ]" : "[ SET AS PILOT ]";
            }

            // Slide in: one frame in the switch state, then the transition runs back.
            if (_feature != null)
            {
                _feature.AddToClassList(SwitchClass);
                _feature.schedule.Execute(() => _feature.RemoveFromClassList(SwitchClass)).StartingIn(16);
            }
        }

        // ---- Actions ----

        private void OpenHangar()
        {
            if (!_context.CanOpenHangar) return;
            Play(_context.Sounds != null ? _context.Sounds.UIClick : null);
            _context.OpenHangar();
        }

        private void StartTraining()
        {
            if (!_model.HasTrainingMap) return;
            Play(_context.Sounds != null ? _context.Sounds.UIClick : null);
            _context.StartTraining(_model.TrainingScene, $"TRAINING // {_model.TrainingMapName}");
        }

        private void OpenCodex() => _model.ShowPage(HomePage.Pilots);

        private void OpenOptions()
        {
            if (options == null) return;
            if (options.IsOpen) options.Close();
            else options.Open();
        }

        private void Back()
        {
            if (OptionsOpen)
            {
                options.Close();
                return;
            }
            if (!_ready)
            {
                FinishBoot();
                return;
            }
            if (_model.Back() && _nav.Count > 0) _nav[3].Button.Focus();
        }

        private void PreviousPilot()
        {
            _model.StepPilot(-1);
            FocusShownRow();
        }

        private void NextPilot()
        {
            _model.StepPilot(1);
            FocusShownRow();
        }

        private void FocusShownRow()
        {
            var shown = _model.ShownPilot;
            if (shown < 0 || shown >= _rows.Count) return;
            _codexList?.ScrollTo(_rows[shown]);
        }

        private void SelectPilot()
        {
            if (!_model.SelectShown()) return;
            Play(_context.Sounds != null ? _context.Sounds.UINavigate : null);
            var shown = _model.Shown;
            if (shown != null) _context.Audio.PlayVoice(shown.Value.SelectLine);
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void Play(Presentation.Audio.SoundCue cue)
        {
            if (cue != null) _context.Audio.Play(cue);
        }

        private static Label Text(string text, string className, bool title)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(className);
            label.AddToClassList(title ? "chopper-title" : "chopper-body");
            return label;
        }
    }
}
