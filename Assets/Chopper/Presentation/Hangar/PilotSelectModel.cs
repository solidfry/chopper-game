using System;
using System.Collections.Generic;
using Chopper.Simulation.Loadout;
using UnityEngine;

namespace Chopper.Presentation.Hangar
{
    /// <summary>One pilot as the select screen shows it.</summary>
    public readonly struct PilotCard
    {
        public readonly ushort Id;
        public readonly string CallSign;
        public readonly string Role;
        public readonly string Passive;
        public readonly string AbilityName;
        public readonly string AbilityText;
        /// <summary>"COOLDOWN 30 S // ON 4 S", or "" with no ability.</summary>
        public readonly string AbilityMeta;
        public readonly Color Accent;
        /// <summary>"+40% REPAIR SPEED" (good) / "+15% BOOST RECHARGE" ... in a fixed order.</summary>
        public readonly IReadOnlyList<(string Text, bool Good)> Effects;
        /// <summary>The big letter(s) on the portrait until there is art: the call sign's first two letters.</summary>
        public readonly string Monogram;
        public readonly Texture2D Portrait;
        public readonly AudioClip SelectLine;
        public readonly AudioClip AbilityLine;
        public readonly bool Locked;
        public readonly int UnlockLevel;

        public PilotCard(PilotDefinition pilot, bool locked)
        {
            Id = pilot.Id;
            CallSign = string.IsNullOrEmpty(pilot.CallSign) ? pilot.name.ToUpperInvariant() : pilot.CallSign;
            Role = pilot.Role ?? "";
            Passive = pilot.Passive ?? "";
            AbilityName = pilot.AbilityName ?? "";
            AbilityText = pilot.AbilityText ?? "";
            var ability = pilot.Ability;
            AbilityMeta = ability.Kind == Simulation.Combat.AbilityKind.None ? ""
                : ability.Duration >= 1f ? $"COOLDOWN {Mathf.RoundToInt(ability.Cooldown)} S // ON {Mathf.RoundToInt(ability.Duration)} S"
                : $"COOLDOWN {Mathf.RoundToInt(ability.Cooldown)} S";
            Accent = pilot.Accent;
            Effects = pilot.Modifiers.Effects();
            Monogram = CallSign.Length >= 2 ? CallSign.Substring(0, 2) : CallSign;
            Portrait = pilot.Portrait;
            SelectLine = pilot.SelectLine;
            AbilityLine = pilot.AbilityLine;
            Locked = locked;
            UnlockLevel = pilot.UnlockLevel;
        }
    }

    /// <summary>
    /// The pilot select screen (plan section 5b, step 2): a grid of pilots, the shown one large (hover, focus or the
    /// cursor), LOCK IN saves it for the next spawn (<see cref="LoadoutChoiceStore.Pilot"/>). Any pilot flies any chassis.
    /// <see cref="Changed"/>: opened, closed or locked in (rebuild); <see cref="ShownChanged"/>: only the big card.
    /// </summary>
    public sealed class PilotSelectModel
    {
        private readonly List<PilotCard> _cards = new();
        private readonly LoadoutChoiceStore _store;

        public PilotSelectModel(IReadOnlyList<PilotDefinition> pilots, LoadoutChoiceStore store, IPlayerUnlocks unlocks)
        {
            _store = store;
            var level = unlocks?.Level ?? int.MaxValue;
            if (pilots != null)
                foreach (var pilot in pilots)
                    if (pilot != null) _cards.Add(new PilotCard(pilot, pilot.UnlockLevel > level));

            var saved = store?.Pilot ?? 0;
            SelectedIndex = _cards.FindIndex(card => card.Id == saved && !card.Locked);
            ShownIndex = Math.Max(SelectedIndex, 0);
        }

        public event Action Changed;
        public event Action ShownChanged;

        public IReadOnlyList<PilotCard> Cards => _cards;
        public bool HasPilots => _cards.Count > 0;
        /// <summary>The locked-in pilot, or -1 (none: the chopper flies without pilot traits).</summary>
        public int SelectedIndex { get; private set; }
        /// <summary>The pilot on the big card.</summary>
        public int ShownIndex { get; private set; }
        public bool IsOpen { get; private set; }

        public PilotCard? Selected => SelectedIndex >= 0 ? _cards[SelectedIndex] : null;
        public PilotCard? Shown => ShownIndex >= 0 && ShownIndex < _cards.Count ? _cards[ShownIndex] : null;
        public ushort SelectedId => Selected?.Id ?? 0;

        public void Open()
        {
            if (IsOpen || !HasPilots) return;
            IsOpen = true;
            ShownIndex = Math.Max(SelectedIndex, 0);
            Changed?.Invoke();
        }

        /// <summary>Closes without a change. False when it was not open.</summary>
        public bool Close()
        {
            if (!IsOpen) return false;
            IsOpen = false;
            Changed?.Invoke();
            return true;
        }

        public void Show(int index)
        {
            if (index < 0 || index >= _cards.Count || index == ShownIndex) return;
            ShownIndex = index;
            ShownChanged?.Invoke();
        }

        /// <summary>Saves the shown pilot and closes. False (nothing changes) for a locked pilot.</summary>
        public bool LockIn()
        {
            var shown = Shown;
            if (shown == null || shown.Value.Locked) return false;
            SelectedIndex = ShownIndex;
            if (_store != null) _store.Pilot = shown.Value.Id;
            IsOpen = false;
            Changed?.Invoke();
            return true;
        }
    }
}
