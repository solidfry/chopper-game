using System.Collections.Generic;
using Chopper.Presentation.Bridge;
using Chopper.Presentation.Hangar;
using UnityEngine;
using UnityEngine.UIElements;

namespace Chopper.UI.Hangar
{
    /// <summary>
    /// The pilot parts of <c>Hangar.uxml</c> (plan section 5b): the PILOT card in the overview, and the fighting-game
    /// style select screen it opens. The shown pilot fills the left (monogram portrait until there is art; the call sign
    /// slams in with a flash; passive, effect chips and the ability card); the roster grid is on the right; hover or
    /// focus shows a pilot, click or LOCK IN picks it. Escape or gamepad B closes it (<see cref="HangarModel.Back"/>).
    /// Made by <see cref="HangarView"/> each time its tree is built.
    /// </summary>
    public sealed class PilotSelectView
    {
        private const string HiddenClass = "hangar--hidden";
        private const string ShownTileClass = "pilot-tile--shown";
        private const string LockedTileClass = "pilot-tile--locked";
        private const string SlamClass = "pilot-feature__callsign--slam";
        private const string FlashClass = "pilot-feature__portrait--flash";
        private const string ScanClass = "pilot-select__scan--run";
        private const string BadEffectClass = "pilot-effect--bad";

        private readonly PilotSelectModel _model;
        private readonly PresentationContext _context;
        private readonly Button _card;
        private readonly Label _cardMono;
        private readonly Label _cardSign;
        private readonly Label _cardRole;
        private readonly VisualElement _screen;
        private readonly VisualElement _scan;
        private readonly VisualElement _portrait;
        private readonly Label _monogram;
        private readonly PilotPortrait _drawn;
        private readonly Label _serial;
        private readonly Label _role;
        private readonly Label _callSign;
        private readonly Label _passive;
        private readonly VisualElement _effects;
        private readonly Label _abilityName;
        private readonly Label _abilityText;
        private readonly Label _abilityMeta;
        private readonly VisualElement _grid;
        private readonly Label _state;
        private readonly Button _lockIn;
        private readonly List<Button> _tiles = new();
        /// <summary>Runs each frame while the select screen is open: moves and draws the live portrait.</summary>
        private IVisualElementScheduledItem _animate;
        private float _lastTick;
        /// <summary>The model's open state as last shown (the screen stays visible while its close plays).</summary>
        private bool _open;
        /// <summary>Glitch on the live portrait when a new pilot shows (it fades out).</summary>
        private float _swapGlitch;

        public PilotSelectView(VisualElement root, PilotSelectModel model, PresentationContext context)
        {
            _model = model;
            _context = context;
            _card = root.Q<Button>("pilot-card");
            _cardMono = root.Q<Label>("pilot-card-mono");
            _cardSign = root.Q<Label>("pilot-card-sign");
            _cardRole = root.Q<Label>("pilot-card-role");
            _screen = root.Q("pilot-select");
            _scan = _screen?.Q(className: "pilot-select__scan");
            _portrait = root.Q("pilot-portrait");
            _monogram = root.Q<Label>("pilot-monogram");
            _drawn = root.Q<PilotPortrait>("pilot-drawn");
            _serial = root.Q<Label>("pilot-serial");
            _role = root.Q<Label>("pilot-role");
            _callSign = root.Q<Label>("pilot-callsign");
            _passive = root.Q<Label>("pilot-passive");
            _effects = root.Q("pilot-effects");
            _abilityName = root.Q<Label>("pilot-ability-name");
            _abilityText = root.Q<Label>("pilot-ability-text");
            _abilityMeta = root.Q<Label>("pilot-ability-meta");
            _grid = root.Q("pilot-grid");
            _state = root.Q<Label>("pilot-state");
            _lockIn = root.Q<Button>("pilot-lock");
            if (_card == null || _screen == null) return;

            _card.clicked += OnCardClicked;
            _lockIn.clicked += OnLockIn;
            _model.Changed += Refresh;
            _model.ShownChanged += OnShownChanged;
            BuildGrid();
            _animate = _portrait.schedule.Execute(Animate).Every(0);
            _animate.Pause();
            Refresh();
        }

        public void Dispose()
        {
            if (_card == null || _screen == null) return;
            _card.clicked -= OnCardClicked;
            _lockIn.clicked -= OnLockIn;
            _model.Changed -= Refresh;
            _model.ShownChanged -= OnShownChanged;
            _animate?.Pause();
        }

        private void OnCardClicked()
        {
            Play(_context.Sounds != null ? _context.Sounds.UIClick : null);
            _model.Open();
        }

        /// <summary>Picked: the scan sound and the pilot's own radio call.</summary>
        private void OnLockIn()
        {
            var shown = _model.Shown;
            if (!_model.LockIn()) return;
            Play(_context.Sounds != null ? _context.Sounds.ChassisScan : null);
            if (shown != null) _context.Audio.PlayVoice(shown.Value.AbilityLine);
        }

        private void Refresh()
        {
            _card.style.display = _model.HasPilots ? DisplayStyle.Flex : DisplayStyle.None;
            var selected = _model.Selected;
            _cardMono.text = selected?.Monogram ?? "--";
            _cardSign.text = selected?.CallSign ?? "NO PILOT";
            _cardRole.text = selected?.Role ?? "PICK ONE: EACH HAS A PASSIVE AND AN ABILITY";
            var accent = selected?.Accent ?? new Color(0f, 1f, 0.58f, 0.35f);
            _card.style.borderLeftColor = accent;
            _cardMono.style.color = accent;

            var open = _model.IsOpen;
            var wasOpen = _open;
            _open = open;
            // The shared window power on / off (WindowTransition): the screen hides when the close ends.
            if (open && !wasOpen) WindowTransition.Open(_screen, HiddenClass);
            if (!open)
            {
                if (!wasOpen) return;
                WindowTransition.Close(_screen, HiddenClass, () =>
                {
                    if (!_open) _animate.Pause();
                });
                _card.Focus();
                return;
            }
            _animate.Resume();
            if (!wasOpen)
            {
                RunScan();
                if (_model.ShownIndex < _tiles.Count) _tiles[_model.ShownIndex].Focus();
            }
            ShowPilot(slam: !wasOpen);
        }

        /// <summary>A new pilot on the big card: the announcer says the call sign (fighting-game style).</summary>
        private void OnShownChanged()
        {
            Play(_context.Sounds != null ? _context.Sounds.UINavigate : null);
            var shown = _model.Shown;
            if (shown != null) _context.Audio.PlayVoice(shown.Value.SelectLine);
            ShowPilot(slam: true);
        }

        private void BuildGrid()
        {
            _grid.Clear();
            _tiles.Clear();
            for (var i = 0; i < _model.Cards.Count; i++)
            {
                var index = i;
                var card = _model.Cards[i];
                var tile = new Button(() =>
                {
                    // A click on the shown pilot locks it in; on another one it shows it first.
                    if (_model.ShownIndex == index) OnLockIn();
                    else _model.Show(index);
                });
                tile.AddToClassList("pilot-tile");
                tile.EnableInClassList(LockedTileClass, card.Locked);
                tile.style.borderBottomColor = card.Accent;
                tile.RegisterCallback<PointerEnterEvent>(_ => _model.Show(index));
                tile.RegisterCallback<FocusInEvent>(_ => _model.Show(index));

                if (card.Portrait != null)
                {
                    // The art, still (only the big card is live): the head fills the whole tile, in the UI green; the
                    // name sits on a strip over the neck.
                    tile.AddToClassList("pilot-tile--art");
                    var art = new VisualElement { pickingMode = PickingMode.Ignore };
                    art.AddToClassList("pilot-tile__drawn");
                    art.AddToClassList("pilot-tile__art");
                    art.style.backgroundImage = new StyleBackground(card.Portrait);
                    var frame = card.FaceFrame;
                    art.RegisterCallback<GeometryChangedEvent>(_ => FrameFace(art, frame));
                    tile.Add(art);
                }
                else
                {
                    var face = new PilotPortrait();
                    face.AddToClassList("pilot-tile__drawn");
                    face.Set(card.Id, card.Accent);
                    tile.Add(face);
                }
                tile.Add(Text(card.CallSign, "pilot-tile__sign", title: true));
                if (card.Locked) tile.Add(Text($"LEVEL {card.UnlockLevel}", "pilot-tile__tag"));
                _grid.Add(tile);
                _tiles.Add(tile);
            }
        }

        private void ShowPilot(bool slam)
        {
            var shown = _model.Shown;
            if (shown == null) return;
            var card = shown.Value;
            for (var i = 0; i < _tiles.Count; i++) _tiles[i].EnableInClassList(ShownTileClass, i == _model.ShownIndex);

            // Art when the pilot has a portrait (live when the rig can draw it); else the drawn helmet with the monogram
            // as a corner tag.
            var rig = _context.PilotPortrait;
            var live = card.Portrait != null && rig.Texture != null;
            if (live)
            {
                card.ShowOn(rig);
                rig.UiColour = false;
                _swapGlitch = slam ? 1f : _swapGlitch;
            }
            var drawn = card.Portrait == null && _drawn != null;
            if (_drawn != null)
            {
                _drawn.style.display = drawn ? DisplayStyle.Flex : DisplayStyle.None;
                _drawn.Set(card.Id, card.Accent);
            }
            _monogram.text = card.Portrait != null ? "" : card.Monogram;
            _monogram.EnableInClassList("pilot-feature__monogram--tag", drawn);
            _monogram.style.color = card.Accent;
            _portrait.style.borderTopColor = _portrait.style.borderBottomColor =
                _portrait.style.borderLeftColor = _portrait.style.borderRightColor = card.Accent;
            _portrait.style.backgroundImage = live ? new StyleBackground(Background.FromRenderTexture(rig.Texture))
                : card.Portrait != null ? new StyleBackground(card.Portrait) : new StyleBackground(StyleKeyword.None);
            _serial.text = $"P-{card.Id:00}";
            _role.text = card.Role;
            _callSign.text = card.CallSign;
            _callSign.style.color = card.Accent;
            _passive.text = card.Passive;
            _abilityName.text = card.AbilityName;
            _abilityText.text = card.AbilityText;
            if (_abilityMeta != null) _abilityMeta.text = card.AbilityMeta;

            _effects.Clear();
            foreach (var (text, good) in card.Effects)
            {
                var chip = Text(text, "pilot-effect");
                chip.EnableInClassList(BadEffectClass, !good);
                _effects.Add(chip);
            }

            var isSelected = _model.ShownIndex == _model.SelectedIndex;
            _state.text = card.Locked ? $"UNLOCKS AT LEVEL {card.UnlockLevel}" : isSelected ? "CURRENT PILOT" : "";
            _lockIn.SetEnabled(!card.Locked);
            _lockIn.text = isSelected ? "[ KEEP ]" : "[ LOCK IN ]";

            if (!slam) return;
            // Slam: jump to the start pose with no transition, then let the USS transition drop it into place.
            _callSign.AddToClassList(SlamClass);
            _portrait.AddToClassList(FlashClass);
            _callSign.schedule.Execute(() =>
            {
                _callSign.RemoveFromClassList(SlamClass);
                _portrait.RemoveFromClassList(FlashClass);
            }).StartingIn(16);
        }

        /// <summary>
        /// The live portrait while the screen is open: it faces the hangar camera (the hangar's light falls on it from
        /// there), and its jaw follows its own radio line (LOCK IN), not the announcer.
        /// </summary>
        private void Animate()
        {
            var now = Time.unscaledTime;
            var deltaTime = _lastTick > 0f ? now - _lastTick : 0f;
            _lastTick = now;
            var rig = _context.PilotPortrait;
            if (!rig.HasPilot) return;
            var camera = UnityEngine.Camera.main;
            var facing = camera != null ? Quaternion.LookRotation(-camera.transform.forward, camera.transform.up) : Quaternion.identity;
            _swapGlitch = Mathf.MoveTowards(_swapGlitch, 0f, deltaTime * 3f);
            var audio = _context.Audio;
            rig.Tick(deltaTime, new Presentation.Pilots.PilotPortraitInput
            {
                Facing = facing,
                Voice = rig.OwnVoice(audio),
                Glitch = _swapGlitch,
            });
        }

        /// <summary>
        /// Sizes and moves the tile's art so the head (top of the head to the base of the neck) fills the tile's height,
        /// the head in the middle, the image's shape kept. If that leaves a gap at a side, the image grows until it
        /// covers the tile. It never moves so far that a gap shows. Background size and position in pixels: they need
        /// the element's size.
        /// </summary>
        internal static void FrameFace(VisualElement art, Vector4 frame)
        {
            var size = art.contentRect.size;
            if (size.x < 1f || size.y < 1f) return;
            var height = size.y / Mathf.Max(0.05f, frame.z - frame.y);
            var width = height * frame.w;
            if (width < size.x)
            {
                height *= size.x / width;
                width = size.x;
            }
            var x = Mathf.Clamp(size.x * 0.5f - frame.x * width, size.x - width, 0f);
            var y = Mathf.Clamp(size.y * 0.5f - (frame.y + frame.z) * 0.5f * height, size.y - height, 0f);
            art.style.backgroundSize = new BackgroundSize(width, height);
            art.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Left, x);
            art.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Top, y);
        }

        /// <summary>The scan band slides down the screen once each time it opens.</summary>
        private void RunScan()
        {
            if (_scan == null) return;
            _scan.RemoveFromClassList(ScanClass);
            _scan.schedule.Execute(() => _scan.AddToClassList(ScanClass)).StartingIn(16);
        }

        private void Play(Presentation.Audio.SoundCue cue)
        {
            if (cue != null) _context.Audio.Play(cue);
        }

        private static Label Text(string text, string className, bool title = false)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(className);
            if (title) label.AddToClassList("chopper-title");
            return label;
        }
    }
}
