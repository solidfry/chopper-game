using System;
using System.Collections.Generic;
using Chopper.Simulation.Loadout;

namespace Chopper.Presentation.Profile
{
    /// <summary>Where an unlock stands for the player.</summary>
    public enum UnlockState : byte
    {
        /// <summary>Reached: the player can use it.</summary>
        Open,
        /// <summary>The next step on its track: shown highlighted.</summary>
        Next,
        Locked,
    }

    /// <summary>One item on an unlock list: a chassis, a pilot or a module.</summary>
    public readonly struct UnlockItem
    {
        public readonly ushort Id;
        public readonly string Name;
        /// <summary>"CHASSIS", "PILOT", or the module slot ("PYLON", "ROTOR" ...).</summary>
        public readonly string Kind;
        public readonly UnlockState State;
        /// <summary>Why it is locked ("LVL 12", "MST 5", "NEEDS ROTOR MK I"); "" when open.</summary>
        public readonly string Reason;

        public UnlockItem(ushort id, string name, string kind, UnlockState state, string reason)
        {
            Id = id;
            Name = name;
            Kind = kind;
            State = state;
            Reason = reason;
        }
    }

    /// <summary>One account level and what it unlocks.</summary>
    public readonly struct UnlockLevel
    {
        public readonly int Level;
        public readonly UnlockState State;
        public readonly IReadOnlyList<UnlockItem> Items;

        public UnlockLevel(int level, UnlockState state, IReadOnlyList<UnlockItem> items)
        {
            Level = level;
            State = state;
            Items = items;
        }
    }

    /// <summary>A part chain for one chassis class: tier 1 first (Rotor Mk I, Mk II, Mk III).</summary>
    public readonly struct UnlockChain
    {
        /// <summary>The slot of the chain ("ROTOR").</summary>
        public readonly string Kind;
        public readonly IReadOnlyList<UnlockItem> Tiers;

        public UnlockChain(string kind, IReadOnlyList<UnlockItem> tiers)
        {
            Kind = kind;
            Tiers = tiers;
        }
    }

    /// <summary>
    /// What progression unlocks, worked out from the catalog and the player's levels (plan section 6a-1):
    /// account levels unlock chassis, pilots and modules; chassis-class mastery and the parent tier unlock part chains.
    /// Pure: no UI, no scene. The same rules as <see cref="LoadoutRules"/> (account level, mastery, parent).
    /// </summary>
    public sealed class UnlocksModel
    {
        /// <summary>The chassis classes in tab order.</summary>
        public static readonly ChassisClass[] Classes = { ChassisClass.Scout, ChassisClass.Gunship, ChassisClass.Assault, ChassisClass.Lifter };

        private readonly LoadoutCatalog _catalog;
        private readonly IPlayerUnlocks _player;
        private readonly ProfileService _profile;

        /// <param name="profile">For the XP bars; null = no bars (tests).</param>
        public UnlocksModel(LoadoutCatalog catalog, IPlayerUnlocks player, ProfileService profile = null)
        {
            _catalog = catalog;
            _player = player;
            _profile = profile;
        }

        // ---- For the view (the UI assembly does not see Simulation types) ----

        public int ClassCount => Classes.Length;
        public string ClassName(int index) => Classes[index].ToString().ToUpperInvariant();
        public List<UnlockChain> ChainsAt(int classIndex) => Chains(Classes[classIndex]);

        /// <summary>Null without a profile.</summary>
        public TrackProgress? AccountTrack => _profile != null ? _profile.Account : null;
        public TrackProgress? ClassTrack(int classIndex) => _profile != null ? _profile.ChassisProgress(Classes[classIndex]) : null;

        /// <summary>Each pilot's mastery bar, in catalog order.</summary>
        public List<TrackProgress> PilotTracks()
        {
            var tracks = new List<TrackProgress>();
            if (_catalog == null || _profile == null) return tracks;
            foreach (var pilot in _catalog.Pilots)
                if (pilot != null) tracks.Add(_profile.PilotProgress(pilot.Id, Upper(pilot.CallSign)));
            return tracks;
        }

        public int Level => _player != null ? _player.Level : 1;

        // ---- Account track ----

        /// <summary>Every account level that unlocks something, lowest first. Level 1 and below count as the start.</summary>
        public List<UnlockLevel> AccountLevels()
        {
            var byLevel = new SortedDictionary<int, List<UnlockItem>>();
            if (_catalog == null) return new List<UnlockLevel>();

            void Add(int level, ushort id, string name, string kind)
            {
                level = Math.Max(level, 1);
                if (!byLevel.TryGetValue(level, out var list)) byLevel[level] = list = new List<UnlockItem>();
                var open = Level >= level;
                list.Add(new UnlockItem(id, name, kind, open ? UnlockState.Open : UnlockState.Locked, open ? "" : $"LVL {level}"));
            }

            foreach (var chassis in _catalog.Chassis)
                if (chassis != null && _catalog.TryGetChassis(chassis.Id, out var spec))
                    Add(spec.UnlockLevel, chassis.Id, Upper(chassis.DisplayName), "CHASSIS");
            foreach (var pilot in _catalog.Pilots)
                if (pilot != null) Add(pilot.UnlockLevel, pilot.Id, Upper(pilot.CallSign), "PILOT");
            foreach (var module in _catalog.Modules)
                // Chain tiers (Mk II, Mk III) need mastery: they show on the chassis tab, not here.
                if (module != null && module.Parent == null)
                    Add(module.UnlockLevel, module.Id, Upper(module.DisplayName), SlotName(module.Slot));

            var next = NextAccountLevel(byLevel.Keys);
            var levels = new List<UnlockLevel>(byLevel.Count);
            foreach (var pair in byLevel)
            {
                var state = pair.Key <= Level ? UnlockState.Open : pair.Key == next ? UnlockState.Next : UnlockState.Locked;
                levels.Add(new UnlockLevel(pair.Key, state, pair.Value));
            }
            return levels;
        }

        /// <summary>"NEXT: RAILGUN AT LVL 12", or "" when nothing is left.</summary>
        public string NextUnlockText()
        {
            foreach (var level in AccountLevels())
            {
                if (level.State != UnlockState.Next || level.Items.Count == 0) continue;
                var more = level.Items.Count > 1 ? $" +{level.Items.Count - 1}" : "";
                return $"NEXT: {level.Items[0].Name}{more} AT LVL {level.Level}";
            }
            return "";
        }

        private int NextAccountLevel(IEnumerable<int> levels)
        {
            foreach (var level in levels)
                if (level > Level) return level;
            return -1;
        }

        // ---- Chassis mastery ----

        public int ChassisLevel(ChassisClass chassisClass) => _player != null ? _player.ChassisLevel(chassisClass) : 1;

        /// <summary>
        /// The part chains a chassis class can fit, with each tier's state for that class. A chain is a module with no
        /// parent followed by its children; single modules show only when they need mastery.
        /// </summary>
        public List<UnlockChain> Chains(ChassisClass chassisClass)
        {
            var chains = new List<UnlockChain>();
            if (_catalog == null) return chains;

            var children = new Dictionary<ModuleDefinition, List<ModuleDefinition>>();
            foreach (var module in _catalog.Modules)
            {
                if (module == null || module.Parent == null) continue;
                if (!children.TryGetValue(module.Parent, out var list)) children[module.Parent] = list = new List<ModuleDefinition>();
                list.Add(module);
            }

            var mastery = ChassisLevel(chassisClass);
            foreach (var root in _catalog.Modules)
            {
                if (root == null || root.Parent != null || !FitsClass(root, chassisClass)) continue;
                // A root shows when a tier after it fits this class, or when it needs mastery itself.
                if (NextTier(children, root, chassisClass) == null && root.MasteryLevel <= 0) continue;

                // Follow the first fitting child each step: chains are a line (Mk I, Mk II, Mk III).
                var tiers = new List<UnlockItem>();
                var parentOpen = true;
                string parentName = null;
                for (var module = root; module != null; module = NextTier(children, module, chassisClass))
                {
                    var tier = Tier(module, mastery, parentOpen, parentName);
                    tiers.Add(tier);
                    parentOpen = tier.State == UnlockState.Open;
                    parentName = tier.Name;
                }
                MarkNext(tiers);
                chains.Add(new UnlockChain(SlotName(root.Slot), tiers));
            }
            chains.Sort((a, b) => string.CompareOrdinal(a.Kind, b.Kind));
            return chains;
        }

        private ModuleDefinition NextTier(Dictionary<ModuleDefinition, List<ModuleDefinition>> children, ModuleDefinition module,
            ChassisClass chassisClass)
        {
            if (!children.TryGetValue(module, out var list)) return null;
            foreach (var child in list)
                if (FitsClass(child, chassisClass)) return child;
            return null;
        }

        private UnlockItem Tier(ModuleDefinition module, int mastery, bool parentOpen, string parentName)
        {
            string reason;
            // Short form ("NEEDS MK II"): the chain row already names the part, and the chip has a fixed width.
            if (!parentOpen)
            {
                var mark = parentName.IndexOf(" MK ", StringComparison.Ordinal);
                reason = $"NEEDS {(mark >= 0 ? parentName.Substring(mark + 1) : parentName)}";
            }
            else if (Level < module.UnlockLevel) reason = $"LVL {module.UnlockLevel}";
            else if (mastery < module.MasteryLevel) reason = $"MST {module.MasteryLevel}";
            else reason = "";
            var state = reason.Length == 0 ? UnlockState.Open : UnlockState.Locked;
            return new UnlockItem(module.Id, Upper(module.DisplayName), SlotName(module.Slot), state, reason);
        }

        /// <summary>The first locked tier whose parent is open becomes Next.</summary>
        private static void MarkNext(List<UnlockItem> tiers)
        {
            for (var i = 0; i < tiers.Count; i++)
            {
                if (tiers[i].State != UnlockState.Locked) continue;
                if (i == 0 || tiers[i - 1].State == UnlockState.Open)
                {
                    var t = tiers[i];
                    tiers[i] = new UnlockItem(t.Id, t.Name, t.Kind, UnlockState.Next, t.Reason);
                }
                return;
            }
        }

        /// <summary>True when some chassis of the class can fit the module.</summary>
        private bool FitsClass(ModuleDefinition module, ChassisClass chassisClass)
        {
            if (!_catalog.TryGetModule(module.Id, out var spec)) return false;
            foreach (var chassis in _catalog.Chassis)
                if (chassis != null && chassis.Class == chassisClass && spec.FitsChassis(chassis.Id)) return true;
            return false;
        }

        // ---- Pilots ----

        public int PilotLevel(ushort pilotId) => _player != null ? _player.PilotLevel(pilotId) : 1;

        private static string SlotName(SlotType slot) => slot.ToString().ToUpperInvariant();
        private static string Upper(string text) => string.IsNullOrEmpty(text) ? "?" : text.ToUpperInvariant();
    }
}
