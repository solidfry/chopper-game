using System;
using System.Collections.Generic;
using Chopper.Simulation.Config;
using Chopper.Simulation.Loadout;
using UnityEngine;

namespace Chopper.Simulation.Bots
{
    /// <summary>One bot of the roster: its name, how well it flies and what it flies.</summary>
    [Serializable]
    public sealed class BotRosterRow
    {
        [Tooltip("Shown in the kill feed and scoreboard (after the name tag). Upper case, at most 24 letters.")]
        public string Name = "BOT";
        [Tooltip("Flight and skill. Empty = the map's bot profile.")]
        public BotProfile Profile;
        [Tooltip("Empty = the next chassis of the catalog.")]
        public ChassisDefinition Chassis;
        [Tooltip("Empty = no pilot.")]
        public PilotDefinition Pilot;
        [Tooltip("One per chassis slot, in slot order (an empty entry = an empty slot). No entries = the chassis's default modules.")]
        public List<ModuleDefinition> Modules = new();

        /// <summary>The chassis this row flies: its own, or the catalog's chassis number <paramref name="slot"/> (wraps).</summary>
        public ChassisDefinition ChassisFor(LoadoutCatalog catalog, int slot)
        {
            if (Chassis != null) return Chassis;
            if (catalog == null || catalog.Chassis.Count == 0) return null;
            return catalog.Chassis[Mathf.Abs(slot) % catalog.Chassis.Count];
        }

        /// <summary>The loadout as a code (empty when there is no chassis: the server then uses its default).</summary>
        public LoadoutCode ToCode(LoadoutCatalog catalog, int slot)
        {
            var chassis = ChassisFor(catalog, slot);
            if (chassis == null) return default;
            var source = Modules != null && Modules.Count > 0 ? (IReadOnlyList<ModuleDefinition>)Modules : chassis.DefaultModules;
            var modules = new ushort[chassis.Slots.Count];
            for (var i = 0; i < modules.Length; i++)
                modules[i] = i < source.Count && source[i] != null ? source[i].Id : LoadoutSelection.EmptySlot;
            return LoadoutCode.From(new LoadoutSelection(chassis.Id, modules), Pilot != null ? Pilot.Id : (ushort)0);
        }
    }

    /// <summary>
    /// Server bots that fill empty player slots: how many, and who they are. One asset, linked from the GameConfig; the
    /// gameplay subscene's spawner bakes it (<see cref="BotFillRules"/>, <see cref="BotRosterEntry"/>; Chopper → Setup,
    /// steps 1 and 4). Clients read the names from the same asset (<see cref="NameOf"/>), so names need no network data.
    /// </summary>
    [CreateAssetMenu(menuName = "Chopper/Bot Settings", fileName = "BotSettings")]
    public sealed class BotSettings : ScriptableObject, IValidatedAsset
    {
        /// <summary>No setting can ask for more (server CPU; see the [Load] log line).</summary>
        public const int HardMaxBots = 8;
        public const int MaxNameLength = 24;

        [SerializeField, Tooltip("Off = the server never adds bots.")]
        private bool fillEmptySlots = true;
        [SerializeField, Min(0), Tooltip("Bots fill the match up to this many players (people + bots). With no people there are no bots.")]
        private int targetPlayers = 4;
        [SerializeField, Range(0, HardMaxBots), Tooltip("Most bots at one time. Each bot costs server CPU like a player and a little more.")]
        private int maxBots = 4;
        [SerializeField, Min(0.1f), Tooltip("Seconds between two bots joining or leaving.")]
        private float stepSeconds = 2f;
        [SerializeField, Tooltip("Shown before each bot name, e.g. [BOT]. Empty = no tag.")]
        private string nameTag = "[BOT]";
        [SerializeField, Tooltip("Bot number 1 is row 1, and so on. More bots than rows: the rows start again.")]
        private List<BotRosterRow> roster = new();

        public bool FillEmptySlots => fillEmptySlots;
        public int TargetPlayers => targetPlayers;
        public int MaxBots => fillEmptySlots ? Mathf.Clamp(maxBots, 0, HardMaxBots) : 0;
        public string NameTag => nameTag;
        public IReadOnlyList<BotRosterRow> Roster => roster;

        public BotFillRules ToRules() => new()
        {
            TargetPlayers = Mathf.Max(targetPlayers, 0),
            MaxBots = MaxBots,
            StepSeconds = Mathf.Max(stepSeconds, 0.1f),
        };

        /// <summary>The name of the bot in <paramref name="slot"/> (see <see cref="BotNames.For"/>).</summary>
        public string NameOfSlot(int slot)
        {
            _names ??= new List<string>();
            _names.Clear();
            foreach (var row in roster) _names.Add(row != null ? row.Name : null);
            return BotNames.For(_names, nameTag, slot);
        }

        /// <summary>The name of the bot with this player id ("" when the id is not a bot's).</summary>
        public string NameOf(int playerId) => BotIds.IsBot(playerId) ? NameOfSlot(BotIds.Slot(playerId)) : "";

        [NonSerialized] private List<string> _names;

        /// <summary>Editor only: used by the setup tool for a new asset.</summary>
        public void Initialise(IEnumerable<BotRosterRow> rows)
        {
            roster = new List<BotRosterRow>(rows);
        }

        public void CollectProblems(List<string> problems)
        {
            if (!fillEmptySlots) return;
            if (maxBots > 0 && targetPlayers < 2)
                problems.Add($"{name}: target players is {targetPlayers}, so no bot ever joins (use 2 or more, or turn the fill off).");
            if (roster.Count == 0)
            {
                problems.Add($"{name}: the bot roster is empty (press '1. Create / repair config' on a new asset, or add rows).");
                return;
            }

            var seen = new HashSet<string>();
            for (var i = 0; i < roster.Count; i++)
            {
                var row = roster[i];
                var label = $"{name}: roster row {i + 1}";
                if (row == null)
                {
                    problems.Add($"{label} is empty.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(row.Name)) problems.Add($"{label} has no name.");
                else
                {
                    if (row.Name.Trim().Length > MaxNameLength) problems.Add($"{label}: the name '{row.Name}' is longer than {MaxNameLength} letters.");
                    if (!seen.Add(row.Name.Trim().ToUpperInvariant())) problems.Add($"{label}: the name '{row.Name}' is used twice.");
                }
                if (row.Modules == null || row.Modules.Count == 0 || row.Chassis == null) continue;
                if (row.Modules.Count > row.Chassis.Slots.Count)
                    problems.Add($"{label}: {row.Modules.Count} modules, but {row.Chassis.name} has {row.Chassis.Slots.Count} slots.");
            }
        }
    }
}
