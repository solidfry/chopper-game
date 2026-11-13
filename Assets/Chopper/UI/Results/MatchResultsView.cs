using System.Collections.Generic;
using Chopper.Presentation.Audio;
using Chopper.Presentation.Bridge;
using Chopper.Presentation.Results;
using Chopper.UI.Cockpit;
using Chopper.UI.Home;
using UnityEngine;
using UnityEngine.UIElements;

namespace Chopper.UI.Results
{
    /// <summary>
    /// The end-of-match screen (<c>MatchResults.uxml</c>), from <see cref="MatchResultsModel"/>. It plays in steps:
    /// title, then the kill / death / score cards count up, then the XP lines come in one by one and the level bar fills
    /// (with LEVEL UP), then an award card for each pilot. PLAY AGAIN hides it until the next match; HOME leaves.
    /// While it shows, the cursor is free and flight input is off (<see cref="PresentationContext.MenuOpen"/>).
    /// </summary>
    [RequireComponent(typeof(PanelRenderer))]
    public sealed class MatchResultsView : MonoBehaviour
    {
        private const string HiddenClass = "results--hidden";
        private const string StepClass = "results--step-";
        private const float CardsAt = 0.55f;
        private const float CountSeconds = 1.2f;
        private const float XpAt = 1.5f;
        private const float XpLineSeconds = 0.3f;
        private const float BarSeconds = 1.6f;
        private const float AwardsAt = 2.1f;
        private const float AwardStagger = 0.14f;

        [SerializeField, Tooltip("Scene presentation bridge; it owns the results model.")]
        private PresentationBridge bridge;

        private PresentationContext _context;
        private MatchResultsModel _model;
        private PanelRenderer _renderer;
        private VisualElement _root;
        private Label _banner;
        private Label _next;
        private Label _kills;
        private Label _deaths;
        private Label _score;
        private Label _pilotName;
        private VisualElement _pilot;
        private VisualElement _pilotPicture;
        private VisualElement _xpLines;
        private Label _xpTotal;
        private Label _level;
        private Label _levelUp;
        private Label _levelNeed;
        private SegmentBar _levelBar;
        /// <summary>Chassis and pilot mastery rows (null with an old layout).</summary>
        private VisualElement _mastery;
        private ScrollView _awards;
        private Button _playAgain;
        private Button _home;
        private CodecScan _scan;
        private readonly List<VisualElement> _lineElements = new();
        private readonly List<VisualElement> _playerElements = new();

        private bool _shown;
        private bool _ownsMenu;
        private float _shownAt;
        private int _step;
        private int _shownLevel;
        private int _barStep;
        private bool _silent;

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
            _model = _context.Results;
            _model.Built += OnBuilt;
            _model.VisibleChanged += RefreshVisible;
            _renderer = GetComponent<PanelRenderer>();
            _renderer.RegisterUIReloadCallback(OnUIReload);
        }

        private void OnDisable()
        {
            if (_renderer != null) _renderer.UnregisterUIReloadCallback(OnUIReload);
            if (_model != null)
            {
                _model.Built -= OnBuilt;
                _model.VisibleChanged -= RefreshVisible;
            }
            Unbind();
            ReleaseMenu();
        }

        private void OnUIReload(PanelRenderer panelRenderer, VisualElement rootElement, int version)
        {
            Unbind();
            _root = rootElement?.Q("results");
            if (_root == null) return;
            _banner = _root.Q<Label>("banner");
            _next = _root.Q<Label>("next");
            _kills = _root.Q<Label>("kills");
            _deaths = _root.Q<Label>("deaths");
            _score = _root.Q<Label>("score");
            _pilot = _root.Q("pilot");
            _pilotPicture = _root.Q("pilot-picture");
            _pilotName = _root.Q<Label>("pilot-name");
            _xpLines = _root.Q("xp-lines");
            _xpTotal = _root.Q<Label>("xp-total");
            _level = _root.Q<Label>("level");
            _levelUp = _root.Q<Label>("level-up");
            _levelNeed = _root.Q<Label>("level-need");
            _levelBar = _root.Q<SegmentBar>("level-bar");
            _mastery = _root.Q("mastery");
            _awards = _root.Q<ScrollView>("awards");
            _scan = _root.Q<CodecScan>("scan");
            _playAgain = _root.Q<Button>("play-again");
            _home = _root.Q<Button>("home");
            _playAgain.clicked += OnPlayAgain;
            _home.clicked += OnHome;
            _shown = false;
            if (_model.Ready) Fill();
            RefreshVisible();
        }

        private void Unbind()
        {
            if (_playAgain != null) _playAgain.clicked -= OnPlayAgain;
            if (_home != null) _home.clicked -= OnHome;
            _playAgain = null;
            _home = null;
            _root = null;
        }

        private void OnBuilt()
        {
            if (_root == null) return;
            Fill();
            // A late stats message while it already shows: the new data, no replay.
            if (_shown) Jump();
        }

        private void RefreshVisible()
        {
            if (_root == null) return;
            var visible = _model.Visible;
            _root.EnableInClassList(HiddenClass, !visible);
            if (visible && !_shown) Begin();
            if (!visible && _shown)
            {
                _shown = false;
                ReleaseMenu();
            }
        }

        private void Begin()
        {
            _shown = true;
            _shownAt = Time.unscaledTime;
            _step = -1;
            SetStep(0);
            _scan?.Burst();
            _context.MenuOpen = true;
            _ownsMenu = true;
            if (_context.CameraRig != null) _context.CameraRig.SetCursorFree(true);
            _home.style.display = _context.CanOpenHome || _context.CanOpenHangar ? DisplayStyle.Flex : DisplayStyle.None;
            _playAgain.Focus();
        }

        private void ReleaseMenu()
        {
            if (!_ownsMenu || _context == null || _context.IsDisposed) return;
            _ownsMenu = false;
            _context.MenuOpen = false;
            if (_context.CameraRig != null) _context.CameraRig.SetCursorFree(false);
        }

        /// <summary>Fills the text and lists from the model; the animation sets the numbers.</summary>
        private void Fill()
        {
            _banner.text = _model.Banner;
            _banner.EnableInClassList("results__banner--won", _model.Won);
            _banner.EnableInClassList("results__banner--lost", !_model.Won && !_model.Draw);
            _pilotName.text = string.IsNullOrEmpty(_context.Match.LocalPilotText) ? "PILOT" : _context.Match.LocalPilotText;

            _xpLines.Clear();
            _lineElements.Clear();
            foreach (var line in _model.XpLines)
            {
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("results-xp-line");
                row.Add(NewLabel(line.Label, "results-xp-line__label", "chopper-title"));
                row.Add(NewLabel($"+{line.Amount}", "results-xp-line__amount", "chopper-title"));
                _xpLines.Add(row);
                _lineElements.Add(row);
            }
            FillMastery();

            _awards.Clear();
            _playerElements.Clear();
            // Own card first, then the rest best first.
            if (_model.Local != null) AddPlayer(_model.Local);
            foreach (var player in _model.Players)
                if (player != _model.Local) AddPlayer(player);
            _shownLevel = 0;
            _barStep = 0;
        }

        private void AddPlayer(ResultsPlayer player)
        {
            var card = new VisualElement { pickingMode = PickingMode.Ignore };
            card.AddToClassList("results-player");
            card.EnableInClassList("results-player--local", player.IsLocal);

            var who = new VisualElement { pickingMode = PickingMode.Ignore };
            who.AddToClassList("results-player__who");
            who.Add(NewLabel(Ordinal(player.Place), "results-player__place", "chopper-title"));
            who.Add(NewLabel(player.IsLocal ? $"{player.Name} (YOU)" : player.Name, "results-player__name", "chopper-title"));
            who.Add(NewLabel($"{player.Kills} K  /  {player.Deaths} D", "results-player__kd", "chopper-body"));
            card.Add(who);

            var list = new VisualElement { pickingMode = PickingMode.Ignore };
            list.AddToClassList("results-player__awards");
            foreach (var award in player.Awards)
            {
                var chip = new VisualElement { pickingMode = PickingMode.Ignore };
                chip.AddToClassList("results-award");
                chip.EnableInClassList("results-award--funny", award.Tone == AwardTone.Funny);
                chip.EnableInClassList("results-award--shame", award.Tone == AwardTone.Shame);
                chip.Add(NewLabel(award.Title, "results-award__title", "chopper-title"));
                chip.Add(NewLabel(award.Line, "results-award__line", "chopper-body"));
                list.Add(chip);
            }
            card.Add(list);
            _awards.Add(card);
            _playerElements.Add(card);
        }

        private void Update()
        {
            if (_root == null || _root.panel == null) return;
            if (_model.Visible) _next.text = $"NEXT MATCH {_context.Match.TimerText}";
            if (!_shown) return;
            RefreshPilot();
            Animate(Time.unscaledTime - _shownAt);
        }

        /// <summary>Everything at its end state (a rebuild while open).</summary>
        private void Jump()
        {
            _shownAt = Time.unscaledTime - 60f;
            _silent = true;
            Animate(60f);
            _silent = false;
        }

        private void Animate(float t)
        {
            SetStep(t >= AwardsAt ? 4 : t >= XpAt ? 3 : t >= CardsAt ? 2 : 1);

            var local = _model.Local;
            var count = Ease(Mathf.Clamp01((t - CardsAt) / CountSeconds));
            var kills = Count(local != null ? local.Kills : 0, count);
            var deaths = Count(local != null ? local.Deaths : 0, count);
            var score = Count(_model.Score, count);
            // A tick each time a number rolls (the cue's cooldown keeps it a fast clatter, not a buzz).
            if (_kills.text != kills || _deaths.text != deaths || _score.text != score) Play(Sounds?.ResultsTick);
            _kills.text = kills;
            _deaths.text = deaths;
            _score.text = score;

            // XP lines one by one; the total follows them.
            var shownXp = 0;
            var lines = _model.XpLines;
            for (var i = 0; i < _lineElements.Count; i++)
            {
                var on = t >= XpAt + XpLineSeconds * (i + 1);
                if (on && !_lineElements[i].ClassListContains("results-xp-line--in")) Ping(i);
                _lineElements[i].EnableInClassList("results-xp-line--in", on);
                if (on && i < lines.Count) shownXp += lines[i].Amount;
            }
            _xpTotal.text = $"+{shownXp}";

            // Then the level bar fills from the old total to the new one.
            var barStart = XpAt + XpLineSeconds * (_lineElements.Count + 1);
            var fill = Ease(Mathf.Clamp01((t - barStart) / BarSeconds));
            var xp = Mathf.RoundToInt(Mathf.Lerp(_model.XpBefore, _model.XpAfter, fill));
            var account = _context.Profile.AccountProgress(xp);
            var level = account.Level;
            var startLevel = _context.Profile.AccountProgress(_model.XpBefore).Level;
            _level.text = $"LVL {level}";
            _levelNeed.text = account.IsMax ? "MAX" : $"{account.Into} / {account.Needed} XP";
            // The bar climbs on from where the lines stopped, one note per step of the fill.
            var barStep = fill > 0f ? Mathf.Min(BarNotes, Mathf.FloorToInt(fill * BarNotes) + 1) : 0;
            if (barStep > _barStep) Ping(_lineElements.Count + barStep - 1);
            _barStep = barStep;
            _levelBar.value = account.Fill;
            // The mastery rows come in after the account bar is full.
            if (_mastery != null) _mastery.EnableInClassList("results-mastery--in", t >= barStart + BarSeconds);
            if (level != _shownLevel)
            {
                if (_shownLevel != 0 && level > _shownLevel)
                {
                    _scan?.Burst();
                    Play(Sounds?.ResultsLevelUp);
                }
                _shownLevel = level;
            }
            _levelUp.EnableInClassList("results-xp__level-up--on", level > startLevel);

            for (var i = 0; i < _playerElements.Count; i++)
            {
                var on = t >= AwardsAt + AwardStagger * i;
                if (on && !_playerElements[i].ClassListContains("results-player--in")) Play(Sounds?.ResultsAward);
                _playerElements[i].EnableInClassList("results-player--in", on);
            }
        }

        private SoundLibrary Sounds => _context.Sounds;

        /// <summary>Notes the level bar plays as it fills.</summary>
        private const int BarNotes = 4;
        /// <summary>
        /// Major pentatonic, in semitones, an octave and a half: any run of it sounds pleasant, and it stops before the
        /// ping turns shrill (more steps stay on the top note).
        /// </summary>
        private static readonly int[] Ladder = { 0, 2, 4, 7, 9, 12, 14, 16, 19 };

        /// <summary>The XP ping, <paramref name="step"/> notes up the ladder (XP lines first, then the bar).</summary>
        private void Ping(int step)
        {
            if (_silent || Sounds == null || Sounds.ResultsXp == null) return;
            var semitones = Ladder[Mathf.Clamp(step, 0, Ladder.Length - 1)];
            _context.Audio.PlayPitched(Sounds.ResultsXp, Mathf.Pow(2f, semitones / 12f));
        }

        /// <summary>A results sound, unless the screen is jumping to its end state.</summary>
        private void Play(SoundCue cue)
        {
            if (_silent || cue == null) return;
            _context.Audio.Play(cue);
        }

        private void SetStep(int step)
        {
            if (step == _step) return;
            // Each panel that pops in: the window sound (not when the screen hides or resets).
            if (step > _step && step > 0) Play(Sounds?.ResultsWindow);
            _step = step;
            for (var i = 1; i <= 4; i++) _root.EnableInClassList(StepClass + i, step >= i);
        }

        /// <summary>The live portrait (<see cref="Presentation.Hud.LocalPilotPortraitSystem"/> keeps drawing it).</summary>
        private void RefreshPilot()
        {
            var rig = _context.PilotPortrait;
            var show = rig != null && rig.HasPilot;
            _pilot.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            // Shot down or no chopper at the end: nothing else draws the portrait, so it idles here.
            if (!_context.Hud.HasChopper)
            {
                var camera = UnityEngine.Camera.main;
                rig.Tick(Time.unscaledDeltaTime, new Presentation.Pilots.PilotPortraitInput
                {
                    Facing = camera != null ? Quaternion.LookRotation(-camera.transform.forward, camera.transform.up) : Quaternion.identity,
                    Voice = rig.OwnVoice(_context.Audio),
                });
            }
            if (_pilotPicture.style.backgroundImage.value.renderTexture != rig.Texture)
                _pilotPicture.style.backgroundImage = Background.FromRenderTexture(rig.Texture);
        }

        private void OnPlayAgain() => _model.Dismiss();

        private void OnHome()
        {
            ReleaseMenu();
            if (_context.CanOpenHome) _context.OpenHome();
            else _context.OpenHangar();
        }

        private static float Ease(float t) => 1f - Mathf.Pow(1f - t, 3f);

        private static string Count(int value, float share) => Mathf.RoundToInt(value * share).ToString();

        /// <summary>One row per mastery track that gained XP: name, level, bar, the XP gained.</summary>
        private void FillMastery()
        {
            if (_mastery == null) return;
            _mastery.Clear();
            _mastery.RemoveFromClassList("results-mastery--in");
            foreach (var gain in _model.Mastery)
            {
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("results-mastery__row");
                var head = new VisualElement { pickingMode = PickingMode.Ignore };
                head.AddToClassList("results-mastery__head");
                head.Add(NewLabel($"{gain.Label} MASTERY", "results-mastery__label", "chopper-title"));
                head.Add(NewLabel(gain.After.LevelText, "results-mastery__level", "chopper-title"));
                if (gain.LevelUp) head.Add(NewLabel("LEVEL UP", "results-mastery__up", "chopper-title"));
                head.Add(NewLabel($"+{gain.Gained}", "results-mastery__gain", "chopper-title"));
                row.Add(head);
                var bar = new SegmentBar { segments = 24, gap = 3f, value = gain.After.Fill, pickingMode = PickingMode.Ignore };
                bar.AddToClassList("results-mastery__bar");
                row.Add(bar);
                _mastery.Add(row);
            }
        }

        private static string Ordinal(int place) => place switch
        {
            1 => "1ST",
            2 => "2ND",
            3 => "3RD",
            _ => $"{place}TH",
        };

        private static Label NewLabel(string text, params string[] classes)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            foreach (var c in classes) label.AddToClassList(c);
            return label;
        }
    }
}
