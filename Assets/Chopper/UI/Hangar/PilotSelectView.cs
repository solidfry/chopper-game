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
        private readonly Label _serial;
        private readonly Label _role;
        private readonly Label _callSign;
        private readonly Label _passive;
        private readonly VisualElement _effects;
        private readonly Label _abilityName;
        private readonly Label _abilityText;
        private readonly VisualElement _grid;
        private readonly Label _state;
        private readonly Button _lockIn;
        private readonly List<Button> _tiles = new();

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
            _serial = root.Q<Label>("pilot-serial");
            _role = root.Q<Label>("pilot-role");
            _callSign = root.Q<Label>("pilot-callsign");
            _passive = root.Q<Label>("pilot-passive");
            _effects = root.Q("pilot-effects");
            _abilityName = root.Q<Label>("pilot-ability-name");
            _abilityText = root.Q<Label>("pilot-ability-text");
            _grid = root.Q("pilot-grid");
            _state = root.Q<Label>("pilot-state");
            _lockIn = root.Q<Button>("pilot-lock");
            if (_card == null || _screen == null) return;

            _card.clicked += OnCardClicked;
            _lockIn.clicked += OnLockIn;
            _model.Changed += Refresh;
            _model.ShownChanged += OnShownChanged;
            BuildGrid();
            Refresh();
        }

        public void Dispose()
        {
            if (_card == null || _screen == null) return;
            _card.clicked -= OnCardClicked;
            _lockIn.clicked -= OnLockIn;
            _model.Changed -= Refresh;
            _model.ShownChanged -= OnShownChanged;
        }

        private void OnCardClicked()
        {
            Play(_context.Sounds != null ? _context.Sounds.UIClick : null);
            _model.Open();
        }

        private void OnLockIn()
        {
            if (_model.LockIn()) Play(_context.Sounds != null ? _context.Sounds.ChassisScan : null);
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
            var wasOpen = !_screen.ClassListContains(HiddenClass);
            _screen.EnableInClassList(HiddenClass, !open);
            if (!open)
            {
                if (wasOpen) _card.Focus();
                return;
            }
            if (!wasOpen)
            {
                RunScan();
                if (_model.ShownIndex < _tiles.Count) _tiles[_model.ShownIndex].Focus();
            }
            ShowPilot(slam: !wasOpen);
        }

        private void OnShownChanged()
        {
            Play(_context.Sounds != null ? _context.Sounds.UINavigate : null);
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

                var mono = Text(card.Monogram, "pilot-tile__mono", title: true);
                mono.style.color = card.Accent;
                tile.Add(mono);
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

            _monogram.text = card.Portrait != null ? "" : card.Monogram;
            _monogram.style.color = card.Accent;
            _portrait.style.borderTopColor = _portrait.style.borderBottomColor =
                _portrait.style.borderLeftColor = _portrait.style.borderRightColor = card.Accent;
            _portrait.style.backgroundImage = card.Portrait != null ? new StyleBackground(card.Portrait) : new StyleBackground(StyleKeyword.None);
            _serial.text = $"P-{card.Id:00}";
            _role.text = card.Role;
            _callSign.text = card.CallSign;
            _callSign.style.color = card.Accent;
            _passive.text = card.Passive;
            _abilityName.text = card.AbilityName;
            _abilityText.text = card.AbilityText;

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
