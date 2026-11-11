using System;
using System.Collections.Generic;
using Chopper.Simulation.Config;
using UnityEngine;

namespace Chopper.Presentation.Menu
{
    /// <summary>Palette accent of a mode card (style guide: the six accent / dark pairs).</summary>
    public enum ModeAccent { Green, Cyan, Yellow, Red, Pink, Blue }

    /// <summary>
    /// Every game mode the PLAY page shows (home screen, <c>page-modes</c>): name, short pitch, rules text, accent, and
    /// the matchmaker queue it joins. A mode that is not <see cref="ModeEntry.Available"/> shows as OFFLINE (planned
    /// modes, plan section 7). The player's pick is saved (<c>LoadoutChoiceStore.MatchMode</c>) and the hangar's FIND
    /// MATCH joins its queue. Setup step 11 makes it with the default modes; the GameConfig points to it.
    /// Rules text is for the menu only: the server's match rules are not read from here (yet).
    /// </summary>
    [CreateAssetMenu(menuName = "Chopper/Game Mode Catalog", fileName = "GameModeCatalog")]
    public sealed class GameModeCatalog : ScriptableObject, IValidatedAsset
    {
        [Serializable]
        public sealed class ModeEntry
        {
            [Tooltip("Saved id of the mode (e.g. quickplay, ffa, tdm, ctn). Never change it once a build is out.")]
            public string Id;
            public string DisplayName;
            [Tooltip("One line under the name, e.g. EVERY PILOT FOR THEMSELVES.")] public string Tagline;
            [TextArea(2, 5), Tooltip("Longer text in sentence case (Chakra Petch).")] public string Description;
            [Tooltip("Rule lines, one per line, e.g. FIRST TO 15 KILLS.")] [TextArea(2, 5)] public string Rules;
            [Tooltip("e.g. 2-8 PILOTS.")] public string Players;
            public ModeAccent Accent;
            [Tooltip("Off: shown as OFFLINE and cannot be picked (the mode is not built yet).")] public bool Available = true;
            [Tooltip("Unity Matchmaker queue. Empty = the hangar's default queue (HangarSettings.MatchQueue).")]
            public string Queue;
        }

        [SerializeField] private List<ModeEntry> modes = new();

        public IReadOnlyList<ModeEntry> Modes => modes;

        public ModeEntry Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var mode in modes)
                if (mode != null && string.Equals(mode.Id, id, StringComparison.OrdinalIgnoreCase)) return mode;
            return null;
        }

        /// <summary>The first mode that can be played; null when none.</summary>
        public ModeEntry FirstAvailable()
        {
            foreach (var mode in modes) if (mode != null && mode.Available) return mode;
            return null;
        }

        /// <summary>The saved mode when it can be played, else the first one that can.</summary>
        public ModeEntry Resolve(string savedId)
        {
            var saved = Find(savedId);
            return saved != null && saved.Available ? saved : FirstAvailable();
        }

        /// <summary>The matchmaker queue of a mode; <paramref name="fallback"/> when it has none.</summary>
        public static string QueueOf(ModeEntry mode, string fallback) =>
            mode != null && !string.IsNullOrWhiteSpace(mode.Queue) ? mode.Queue.Trim() : fallback;

        /// <summary>Editor only: the setup tool adds a default mode (no change when the id is listed).</summary>
        public bool Add(ModeEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Id) || Find(entry.Id) != null) return false;
            modes.Add(entry);
            return true;
        }

        public void CollectProblems(List<string> problems)
        {
            if (modes.Count == 0) problems.Add($"{name}: no game modes.");
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var mode in modes)
            {
                if (mode == null) continue;
                if (string.IsNullOrEmpty(mode.Id)) problems.Add($"{name}: a mode has no id.");
                else if (!ids.Add(mode.Id)) problems.Add($"{name}: mode id '{mode.Id}' is used twice.");
                if (string.IsNullOrEmpty(mode.DisplayName)) problems.Add($"{name}: mode '{mode.Id}' has no name.");
            }
            if (FirstAvailable() == null) problems.Add($"{name}: no mode is available (PLAY has nothing to pick).");
        }
    }
}
