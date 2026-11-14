using System;
using System.Collections.Generic;
using Chopper.Presentation.Visuals;
using Chopper.Simulation.Loadout;
using Chopper.Simulation.Session;

namespace Chopper.Presentation.Hangar
{
    /// <summary>A chassis slot for the UI (which does not reference simulation).</summary>
    public readonly struct HangarSlotRow
    {
        public readonly string Label;
        public readonly string Module;
        public readonly bool Empty;

        public HangarSlotRow(string label, string module, bool empty)
        {
            Label = label;
            Module = module;
            Empty = empty;
        }
    }

    /// <summary>A module the selected slot can take (or "empty").</summary>
    public readonly struct HangarOptionRow
    {
        public readonly string Name;
        public readonly string Description;
        /// <summary>Weight cost for the tile corner ("8"); "0" for empty.</summary>
        public readonly string Cost;
        /// <summary>"LEVEL 20" when locked, else the weight ("8 KG").</summary>
        public readonly string Detail;
        public readonly bool Locked;
        public readonly bool Fitted;
        /// <summary>The "no module" choice.</summary>
        public readonly bool Empty;

        public HangarOptionRow(string name, string description, string cost, string detail, bool locked, bool fitted, bool empty)
        {
            Name = name;
            Description = description;
            Cost = cost;
            Detail = detail;
            Locked = locked;
            Fitted = fitted;
            Empty = empty;
        }
    }

    /// <summary>
    /// A stat: now and, while a module is hovered, with that module fitted. Fractions 0..1. Headline stats show as big
    /// numbers next to the chopper; the others as bars.
    /// </summary>
    public readonly struct HangarStatRow
    {
        public readonly string Label;
        public readonly float Fraction;
        public readonly float PreviewFraction;
        public readonly string Value;
        public readonly string Unit;
        public readonly bool Headline;

        public HangarStatRow(string label, float fraction, float previewFraction, string value, string unit, bool headline)
        {
            Label = label;
            Fraction = fraction;
            PreviewFraction = previewFraction;
            Value = value;
            Unit = unit;
            Headline = headline;
        }

        public string Text => $"{Value} {Unit}";
    }

    /// <summary>A skin tile: name, pattern, its main colours (swatch), lock and whether it is the one picked.</summary>
    public readonly struct HangarSkinRow
    {
        public readonly string Name;
        public readonly string Pattern;
        public readonly IReadOnlyList<UnityEngine.Color> Colours;
        public readonly bool Locked;
        public readonly int UnlockLevel;
        public readonly bool Selected;

        public HangarSkinRow(string name, string pattern, IReadOnlyList<UnityEngine.Color> colours, bool locked, int unlockLevel, bool selected)
        {
            Name = name;
            Pattern = pattern;
            Colours = colours;
            Locked = locked;
            UnlockLevel = unlockLevel;
            Selected = selected;
        }
    }

    /// <summary>A chassis card: name, slot count, weight budget and the class mastery level.</summary>
    public readonly struct HangarChassisCard
    {
        public readonly string Name;
        /// <summary>"6"; "" when not known.</summary>
        public readonly string Slots;
        /// <summary>"60"; "" when not known.</summary>
        public readonly string Weight;
        /// <summary>Class mastery level; 0 = not shown (no profile or no class).</summary>
        public readonly int Mastery;

        public HangarChassisCard(string name, string slots, string weight, int mastery)
        {
            Name = name;
            Slots = slots;
            Weight = weight;
            Mastery = mastery;
        }
    }

    /// <summary>
    /// Until progression (Phase 5): the hangar shows unlock levels but lets you fit everything. The server checks
    /// unlocks on its side.
    /// </summary>
    public sealed class AllUnlocked : IPlayerUnlocks
    {
        public int Level => int.MaxValue;
        public bool Owns(ushort moduleId) => true;
    }

    /// <summary>
    /// The hangar (plan section 5): pick a chassis, focus a slot, fit a module, pick a skin, launch. Uses the same
    /// catalog and <see cref="LoadoutRules"/> as the server, so the stats and errors shown are the ones that apply.
    /// Each chassis keeps its own build (saved, so it stays across sessions) and its named builds (presets).
    /// <see cref="Changed"/>: the build, focus, skin or presets changed (rebuild lists and the 3D view);
    /// <see cref="StatsChanged"/>: only the hover preview changed (stats and the module detail).
    /// </summary>
    public sealed class HangarModel
    {
        public const int Overview = -1;
        private const float MetresPerSecondToKilometresPerHour = 3.6f;
        /// <summary>Full bar = this much above the best chassis with its default modules (room for better modules).</summary>
        private const float BarHeadroom = 1.25f;

        private readonly LoadoutCatalog _catalog;
        private readonly SkinLibrary _skins;
        private readonly LoadoutChoiceStore _store;
        private readonly IPlayerUnlocks _unlocks;
        private readonly Profile.ProfileService _profile;
        private readonly List<MapCatalog.MapEntry> _trainingMaps = new();
        private readonly List<string> _trainingMapNames = new();
        private readonly Dictionary<ushort, ModuleDefinition[]> _fitted = new();
        private readonly Dictionary<ushort, List<LoadoutPreset>> _presets = new();
        /// <summary>Choices for the focused slot; index 0 is "empty" (null).</summary>
        private readonly List<ModuleDefinition> _options = new();
        private readonly List<string> _chassisNames = new();
        private readonly List<HangarChassisCard> _chassisCards = new();
        private readonly List<string> _skinNames = new();
        private readonly List<string> _presetNames = new();
        private readonly List<HangarSkinRow> _skinRows = new();
        private readonly List<HangarSlotRow> _slotRows = new();
        private readonly List<HangarOptionRow> _optionRows = new();
        private readonly List<HangarStatRow> _statRows = new();
        private readonly float _maxHealth;
        private float _maxSpeed = 1f;
        private float _maxTurn = 1f;
        private float _maxClimb = 1f;
        private float _maxBoost = 1f;
        private float _minRefill = 1f;
        private int _hovered = -1;

        /// <param name="profile">For the mastery level on the chassis and pilot cards. Null = not shown.</param>
        public HangarModel(LoadoutCatalog catalog, SkinLibrary skins, LoadoutChoiceStore store, IPlayerUnlocks unlocks,
            MapCatalog maps = null, Profile.ProfileService profile = null)
        {
            _profile = profile;
            _catalog = catalog;
            _skins = skins;
            _store = store;
            _unlocks = unlocks ?? new AllUnlocked();
            if (maps != null)
                foreach (var map in maps.Training())
                {
                    _trainingMaps.Add(map);
                    _trainingMapNames.Add((string.IsNullOrEmpty(map.DisplayName) ? map.Scene : map.DisplayName).ToUpperInvariant());
                }
            var savedMap = _store?.TrainingMap;
            TrainingMapIndex = Math.Max(0, _trainingMaps.FindIndex(m => m.Scene == savedMap));

            foreach (var chassis in _catalog.Chassis)
            {
                _chassisNames.Add(chassis != null ? chassis.DisplayName.ToUpperInvariant() : "?");
                _chassisCards.Add(Card(chassis));
            }
            if (_skins != null)
                foreach (var skin in _skins.Skins) _skinNames.Add(skin.DisplayName.ToUpperInvariant());
            _maxHealth = MaxHealth();
            BarScales();

            Pilots = new PilotSelectModel(_catalog.Pilots, _store, _unlocks, _profile);
            ChassisIndex = 0;
            RestoreBuilds();
            if (_store != null && _store.TryLoad(out var saved, out var skinId)) Restore(saved, skinId);
            Refresh();
        }

        public event Action Changed;
        public event Action StatsChanged;
        /// <summary>The pilot select screen and the locked-in pilot (plan section 5b).</summary>
        public PilotSelectModel Pilots { get; }
        /// <summary>A valid loadout was saved; the scene starts the match.</summary>
        public event Action Launched;
        /// <summary>A valid loadout was saved and the player asked for an online match; the scene runs the search.</summary>
        public event Action MatchRequested;
        /// <summary>The player cancelled the search.</summary>
        public event Action SearchCancelled;
        /// <summary><see cref="Searching"/> or <see cref="MatchStatus"/> changed.</summary>
        public event Action MatchStatusChanged;

        public IReadOnlyList<string> ChassisNames => _chassisNames;
        public IReadOnlyList<HangarChassisCard> ChassisCards => _chassisCards;
        public int ChassisIndex { get; private set; }
        public ChassisDefinition Chassis => ChassisIndex < _catalog.Chassis.Count ? _catalog.Chassis[ChassisIndex] : null;
        public string ChassisDescription => Chassis != null ? Chassis.Description : "";
        /// <summary>One module per slot (null = empty), same order as the chassis slots.</summary>
        public IReadOnlyList<ModuleDefinition> Fitted => Chassis != null ? FittedFor(Chassis) : Array.Empty<ModuleDefinition>();
        public int FocusedSlot { get; private set; } = Overview;
        public IReadOnlyList<HangarSlotRow> Slots => _slotRows;
        public IReadOnlyList<HangarOptionRow> Options => _optionRows;
        public IReadOnlyList<HangarStatRow> Stats => _statRows;
        public string FocusTitle => FocusedSlot == Overview ? "" : _slotRows[FocusedSlot].Label;
        /// <summary>The option the detail block shows: the hovered one, else the fitted one; -1 in the overview.</summary>
        public int DetailOption
        {
            get
            {
                if (FocusedSlot == Overview) return -1;
                if (_hovered >= 0) return _hovered;
                for (var i = 0; i < _optionRows.Count; i++)
                    if (_optionRows[i].Fitted) return i;
                return -1;
            }
        }
        /// <summary>Named builds of the current chassis.</summary>
        public IReadOnlyList<string> PresetNames => _presetNames;
        /// <summary>Maps the player can train on (hangar MAP list); empty without a map catalog.</summary>
        public IReadOnlyList<string> TrainingMapNames => _trainingMapNames;
        public int TrainingMapIndex { get; private set; }
        /// <summary>Scene of the picked training map; null without a map catalog (the scene's default then).</summary>
        public string TrainingScene => TrainingMapIndex < _trainingMaps.Count ? _trainingMaps[TrainingMapIndex].Scene : null;

        /// <summary>Picks the training map (saved for next time).</summary>
        public void SetTrainingMap(int index)
        {
            if (index < 0 || index >= _trainingMaps.Count || index == TrainingMapIndex) return;
            TrainingMapIndex = index;
            if (_store != null) _store.TrainingMap = _trainingMaps[index].Scene;
        }
        public float WeightFraction { get; private set; }
        public float PreviewWeightFraction { get; private set; }
        public string WeightText { get; private set; } = "";
        public bool OverWeight { get; private set; }
        public IReadOnlyList<string> SkinNames => _skinNames;
        /// <summary>One tile per skin (same order as <see cref="SkinNames"/>).</summary>
        public IReadOnlyList<HangarSkinRow> Skins => _skinRows;
        public int SkinIndex { get; private set; }
        public ushort SkinId => _skins != null && SkinIndex < _skins.Skins.Count ? _skins.Skins[SkinIndex].Id : (ushort)0;
        /// <summary>Why the loadout cannot launch; empty when it can.</summary>
        public string Error { get; private set; } = "";
        public bool CanLaunch => Error.Length == 0;
        /// <summary>An online search runs (launch and find match are off; Escape cancels).</summary>
        public bool Searching { get; private set; }
        /// <summary>What the search is doing, or why it stopped; empty when idle.</summary>
        public string MatchStatus { get; private set; } = "";

        public void SelectChassis(int index)
        {
            if (index < 0 || index >= _catalog.Chassis.Count || index == ChassisIndex) return;
            ChassisIndex = index;
            FocusedSlot = Overview;
            Refresh();
        }

        public void FocusSlot(int slot)
        {
            if (Chassis == null || slot < Overview || slot >= Chassis.Slots.Count || slot == FocusedSlot) return;
            FocusedSlot = slot;
            Refresh();
        }

        /// <summary>Close the pilot select screen, else back out of a slot. False when already in the overview.</summary>
        public bool Back()
        {
            if (Pilots.Close()) return true;
            if (FocusedSlot == Overview) return false;
            FocusedSlot = Overview;
            Refresh();
            return true;
        }

        /// <summary>Fits option <paramref name="option"/> (index into <see cref="Options"/>) in the focused slot. Locked: nothing.</summary>
        public void Fit(int option)
        {
            if (FocusedSlot == Overview || option < 0 || option >= _options.Count || _optionRows[option].Locked) return;
            FittedFor(Chassis)[FocusedSlot] = _options[option];
            SaveBuild();
            Refresh();
        }

        /// <summary>Shows the stats as if option <paramref name="option"/> were fitted; -1 clears the preview.</summary>
        public void Hover(int option)
        {
            if (option >= _options.Count || FocusedSlot == Overview) option = -1;
            if (option == _hovered) return;
            _hovered = option;
            RefreshStats();
            StatsChanged?.Invoke();
        }

        /// <summary>Locked skins (player level below the skin's unlock level) cannot be picked.</summary>
        public void SelectSkin(int index)
        {
            if (index < 0 || index >= _skinNames.Count || index == SkinIndex) return;
            if (_skins.Skins[index].UnlockLevel > _unlocks.Level) return;
            SkinIndex = index;
            Refresh();
        }

        /// <summary>
        /// Saves the current build under <paramref name="name"/> (cleaned, see <see cref="LoadoutChoiceStore.CleanName"/>).
        /// A preset with the same name is replaced. Returns its index, or -1 for an empty name.
        /// </summary>
        public int SavePreset(string name)
        {
            name = LoadoutChoiceStore.CleanName(name);
            if (name.Length == 0 || Chassis == null) return -1;
            var presets = PresetsFor(Chassis);
            var preset = new LoadoutPreset(name, Ids(FittedFor(Chassis)));
            var index = presets.FindIndex(p => p.Name == name);
            if (index >= 0) presets[index] = preset;
            else
            {
                presets.Add(preset);
                index = presets.Count - 1;
            }
            StorePresets();
            Refresh();
            return index;
        }

        /// <summary>Fits the modules of preset <paramref name="index"/>. Modules no longer in the catalog become empty.</summary>
        public void ApplyPreset(int index)
        {
            if (Chassis == null) return;
            var presets = PresetsFor(Chassis);
            if (index < 0 || index >= presets.Count) return;
            var fitted = FittedFor(Chassis);
            var modules = presets[index].Modules;
            for (var slot = 0; slot < fitted.Length; slot++)
                fitted[slot] = slot < modules.Length ? Module(modules[slot]) : null;
            SaveBuild();
            Refresh();
        }

        /// <summary>False for an empty name or a name that another preset has.</summary>
        public bool RenamePreset(int index, string name)
        {
            name = LoadoutChoiceStore.CleanName(name);
            if (Chassis == null || name.Length == 0) return false;
            var presets = PresetsFor(Chassis);
            if (index < 0 || index >= presets.Count) return false;
            var other = presets.FindIndex(p => p.Name == name);
            if (other >= 0 && other != index) return false;
            presets[index] = new LoadoutPreset(name, presets[index].Modules);
            StorePresets();
            Refresh();
            return true;
        }

        public void DeletePreset(int index)
        {
            if (Chassis == null) return;
            var presets = PresetsFor(Chassis);
            if (index < 0 || index >= presets.Count) return;
            presets.RemoveAt(index);
            StorePresets();
            Refresh();
        }

        /// <summary>
        /// Saves the loadout for the next spawn (the client world reads it when it starts). False (nothing saved) when
        /// it is not valid. The scene calls it again when a match is found: changes made while searching count.
        /// </summary>
        public bool SaveChoice()
        {
            if (!CanLaunch) return false;
            _store?.Save(Selection(FittedFor(Chassis)), SkinId, Chassis.Class);
            return true;
        }

        /// <summary>Saves the loadout and raises <see cref="MatchRequested"/>. False when it is not valid or a search runs.</summary>
        public bool FindMatch()
        {
            if (Searching || !SaveChoice()) return false;
            SetMatchStatus("SEARCHING", true);
            MatchRequested?.Invoke();
            return true;
        }

        /// <summary>Asks the scene to stop the search (it then calls <see cref="SetMatchStatus"/>).</summary>
        public void CancelSearch()
        {
            if (Searching) SearchCancelled?.Invoke();
        }

        /// <summary>Set by the scene while it searches, and when the search ends (searching false).</summary>
        public void SetMatchStatus(string status, bool searching)
        {
            if (status == MatchStatus && searching == Searching) return;
            MatchStatus = status ?? "";
            Searching = searching;
            MatchStatusChanged?.Invoke();
        }

        /// <summary>Saves the loadout and raises <see cref="Launched"/>. False (nothing saved) when it is not valid.</summary>
        public bool Launch()
        {
            if (Searching || !SaveChoice()) return false;
            Launched?.Invoke();
            return true;
        }

        /// <summary>Each chassis's saved build and presets (a build that no longer matches the chassis's slots is dropped).</summary>
        private void RestoreBuilds()
        {
            if (_store == null) return;
            foreach (var chassis in _catalog.Chassis)
            {
                if (chassis == null) continue;
                _presets[chassis.Id] = _store.LoadPresets(chassis.Id);
                if (!_store.TryLoadBuild(chassis.Id, out var build) || build.Modules.Count != chassis.Slots.Count) continue;
                var modules = new ModuleDefinition[chassis.Slots.Count];
                for (var slot = 0; slot < modules.Length; slot++) modules[slot] = Module(build.Modules[slot]);
                _fitted[chassis.Id] = modules;
            }
        }

        /// <summary>The saved choice for the next spawn: its chassis shows first, with its modules, and its skin.</summary>
        private void Restore(in LoadoutSelection saved, ushort skinId)
        {
            for (var i = 0; i < _catalog.Chassis.Count; i++)
            {
                var chassis = _catalog.Chassis[i];
                if (chassis == null || chassis.Id != saved.ChassisId || saved.Modules.Count != chassis.Slots.Count) continue;
                // The chassis's own build is newer when it was changed after the last launch.
                if (!_fitted.ContainsKey(chassis.Id))
                {
                    var modules = new ModuleDefinition[chassis.Slots.Count];
                    for (var slot = 0; slot < modules.Length; slot++) modules[slot] = Module(saved.Modules[slot]);
                    _fitted[chassis.Id] = modules;
                }
                ChassisIndex = i;
                break;
            }

            if (_skins == null) return;
            for (var i = 0; i < _skins.Skins.Count; i++)
                if (_skins.Skins[i].Id == skinId) SkinIndex = i;
        }

        private void SaveBuild() => _store?.SaveBuild(Selection(FittedFor(Chassis)));

        private List<LoadoutPreset> PresetsFor(ChassisDefinition chassis)
        {
            if (_presets.TryGetValue(chassis.Id, out var presets)) return presets;
            presets = new List<LoadoutPreset>();
            _presets[chassis.Id] = presets;
            return presets;
        }

        private void StorePresets() => _store?.SavePresets(Chassis.Id, PresetsFor(Chassis));

        private ModuleDefinition[] FittedFor(ChassisDefinition chassis)
        {
            if (_fitted.TryGetValue(chassis.Id, out var modules)) return modules;
            modules = new ModuleDefinition[chassis.Slots.Count];
            for (var slot = 0; slot < modules.Length && slot < chassis.DefaultModules.Count; slot++) modules[slot] = chassis.DefaultModules[slot];
            _fitted[chassis.Id] = modules;
            return modules;
        }

        private HangarChassisCard Card(ChassisDefinition chassis)
        {
            if (chassis == null || !_catalog.TryGetChassis(chassis.Id, out var spec)) return new HangarChassisCard("?", "", "", 0);
            var mastery = _profile != null && chassis.Class != ChassisClass.None ? _profile.ChassisLevel(chassis.Class) : 0;
            return new HangarChassisCard(chassis.DisplayName.ToUpperInvariant(),
                chassis.Slots.Count.ToString(), spec.WeightBudget.ToString("0"), mastery);
        }

        private void Refresh()
        {
            _hovered = -1;
            BuildPresetNames();
            BuildSkins();
            BuildSlots();
            BuildOptions();
            RefreshStats();
            Changed?.Invoke();
        }

        private void BuildSkins()
        {
            _skinRows.Clear();
            if (_skins == null) return;
            for (var i = 0; i < _skins.Skins.Count; i++)
            {
                var skin = _skins.Skins[i];
                var look = skin.Look;
                var colours = look != null && look.Colours != null ? look.Colours : Array.Empty<UnityEngine.Color>();
                var pattern = look != null ? look.Pattern.ToString().ToUpperInvariant() : "";
                _skinRows.Add(new HangarSkinRow(_skinNames[i], pattern, colours, skin.UnlockLevel > _unlocks.Level, skin.UnlockLevel,
                    i == SkinIndex));
            }
        }

        private void BuildPresetNames()
        {
            _presetNames.Clear();
            if (Chassis == null) return;
            foreach (var preset in PresetsFor(Chassis)) _presetNames.Add(preset.Name);
        }

        private void BuildSlots()
        {
            _slotRows.Clear();
            var chassis = Chassis;
            if (chassis == null) return;
            var fitted = FittedFor(chassis);
            for (var slot = 0; slot < chassis.Slots.Count; slot++)
            {
                var type = chassis.Slots[slot];
                var count = 0;
                var ordinal = 0;
                for (var other = 0; other < chassis.Slots.Count; other++)
                {
                    if (chassis.Slots[other] != type) continue;
                    count++;
                    if (other < slot) ordinal++;
                }
                var label = count > 1 ? $"{type.ToString().ToUpperInvariant()} {ordinal + 1}" : type.ToString().ToUpperInvariant();
                var module = fitted[slot];
                _slotRows.Add(new HangarSlotRow(label, module != null ? module.DisplayName.ToUpperInvariant() : "EMPTY", module == null));
            }
        }

        private void BuildOptions()
        {
            _options.Clear();
            _optionRows.Clear();
            if (FocusedSlot == Overview || Chassis == null) return;

            var type = Chassis.Slots[FocusedSlot];
            var current = FittedFor(Chassis)[FocusedSlot];
            _options.Add(null);
            _optionRows.Add(new HangarOptionRow("EMPTY", "No module in this slot. Saves weight.", "0", "0 KG", false, current == null, true));

            var candidates = new List<(ModuleDefinition Module, ModuleSpec Spec)>();
            foreach (var module in _catalog.Modules)
            {
                if (module == null || module.Slot != type || !_catalog.TryGetModule(module.Id, out var spec) || !spec.FitsChassis(Chassis.Id)) continue;
                candidates.Add((module, spec));
            }
            // Account level first, then the part chain order (mastery level), then the name.
            candidates.Sort((a, b) => a.Spec.UnlockLevel != b.Spec.UnlockLevel
                ? a.Spec.UnlockLevel.CompareTo(b.Spec.UnlockLevel)
                : a.Spec.MasteryLevel != b.Spec.MasteryLevel
                    ? a.Spec.MasteryLevel.CompareTo(b.Spec.MasteryLevel)
                    : string.CompareOrdinal(a.Module.DisplayName, b.Module.DisplayName));

            foreach (var (module, spec) in candidates)
            {
                var levelLocked = _unlocks.Level < spec.UnlockLevel;
                var masteryLocked = spec.MasteryLevel > 0 && _unlocks.ChassisLevel(Chassis.Class) < spec.MasteryLevel;
                var locked = levelLocked || masteryLocked || !_unlocks.Owns(module.Id);
                var detail = levelLocked ? $"LEVEL {spec.UnlockLevel}"
                    : masteryLocked ? $"MASTERY {spec.MasteryLevel}"
                    : locked ? "LOCKED"
                    : $"{spec.Stats.Weight:0} KG";
                var description = module.Description ?? "";
                // A part chain tier names the tier before it.
                if (module.Parent != null) description = $"UPGRADE OF {module.Parent.DisplayName.ToUpperInvariant()}. {description}";
                // Weapons and optics: their numbers after the text.
                var summary = ModuleSummary.Text(module);
                if (summary.Length > 0) description = description.Length > 0 ? $"{description}\n{summary}" : summary;
                _options.Add(module);
                _optionRows.Add(new HangarOptionRow(module.DisplayName.ToUpperInvariant(), description,
                    $"{spec.Stats.Weight:0}", detail, locked, module == current, false));
            }
        }

        private void RefreshStats()
        {
            _statRows.Clear();
            var chassis = Chassis;
            if (chassis == null || !_catalog.TryGetChassis(chassis.Id, out var spec))
            {
                Error = "NO CHASSIS";
                return;
            }

            var fitted = FittedFor(chassis);
            var selection = Selection(fitted);
            var now = LoadoutRules.Compute(spec, selection, _catalog);
            var preview = now;
            if (_hovered >= 0 && FocusedSlot != Overview)
            {
                var modules = new ushort[fitted.Length];
                for (var i = 0; i < modules.Length; i++) modules[i] = selection.Modules[i];
                modules[FocusedSlot] = _options[_hovered] != null ? _options[_hovered].Id : LoadoutSelection.EmptySlot;
                preview = LoadoutRules.Compute(spec, new LoadoutSelection(chassis.Id, modules), _catalog);
            }

            // Real units from the same maths the chopper flies with (FlightEstimate). A longer bar is better everywhere.
            var flight = chassis.Flight;
            AddStat("HULL", now.Health / _maxHealth, preview.Health / _maxHealth, $"{preview.Health:0}", "HP", true);
            var speed = FlightEstimate.TopSpeed(flight, preview) * MetresPerSecondToKilometresPerHour;
            AddStat("SPEED", FlightEstimate.TopSpeed(flight, now) / _maxSpeed, FlightEstimate.TopSpeed(flight, preview) / _maxSpeed,
                $"{speed:0}", "KM/H", true);
            AddStat("TURN", FlightEstimate.TurnRate(flight, now) / _maxTurn, FlightEstimate.TurnRate(flight, preview) / _maxTurn,
                $"{FlightEstimate.TurnRate(flight, preview):0}", "DEG/S", true);
            AddStat("CLIMB", FlightEstimate.Climb(flight, now) / _maxClimb, FlightEstimate.Climb(flight, preview) / _maxClimb,
                $"{FlightEstimate.Climb(flight, preview):0.0}", "M/S2", false);
            AddStat("BOOST", FlightEstimate.BoostPower(flight, now) / _maxBoost, FlightEstimate.BoostPower(flight, preview) / _maxBoost,
                $"{FlightEstimate.BoostPower(flight, preview):0}", "M/S2", false);
            // Refill time: shorter is better, so the bar is the fastest refill over this one.
            AddStat("REFILL", _minRefill / FlightEstimate.BoostRefill(flight, now), _minRefill / FlightEstimate.BoostRefill(flight, preview),
                $"{FlightEstimate.BoostRefill(flight, preview):0.0}", "S", false);

            WeightFraction = now.WeightFraction;
            PreviewWeightFraction = preview.WeightFraction;
            WeightText = $"{preview.Weight:0} / {preview.WeightBudget:0} KG";
            OverWeight = now.Weight > now.WeightBudget;

            var result = LoadoutRules.Validate(selection, _catalog, _unlocks);
            Error = result.IsValid ? "" : Describe(result);
        }

        private void AddStat(string label, float now, float preview, string value, string unit, bool headline) =>
            _statRows.Add(new HangarStatRow(label, Math.Clamp(now, 0f, 1f), Math.Clamp(preview, 0f, 1f), value, unit, headline));

        private string Describe(in LoadoutResult result)
        {
            var slot = result.Slot >= 0 && result.Slot < _slotRows.Count ? _slotRows[result.Slot].Label : "";
            return result.Error switch
            {
                LoadoutError.OverWeight => "OVER WEIGHT: REMOVE A MODULE",
                LoadoutError.ChassisLocked => "CHASSIS LOCKED",
                LoadoutError.ModuleLocked or LoadoutError.NotOwned => $"{slot}: MODULE LOCKED",
                LoadoutError.MasteryLocked => $"{slot}: NEEDS MORE CHASSIS MASTERY",
                LoadoutError.WrongSlotType or LoadoutError.WrongChassis => $"{slot}: MODULE DOES NOT FIT",
                _ => result.ToString().ToUpperInvariant(),
            };
        }

        private LoadoutSelection Selection(ModuleDefinition[] fitted) => new(Chassis.Id, Ids(fitted));

        private static ushort[] Ids(ModuleDefinition[] fitted)
        {
            var ids = new ushort[fitted.Length];
            for (var i = 0; i < ids.Length; i++) ids[i] = fitted[i] != null ? fitted[i].Id : LoadoutSelection.EmptySlot;
            return ids;
        }

        private ModuleDefinition Module(ushort id)
        {
            if (id == LoadoutSelection.EmptySlot) return null;
            foreach (var module in _catalog.Modules)
                if (module != null && module.Id == id) return module;
            return null;
        }

        /// <summary>Bar lengths: each stat of every chassis with its default modules; the best one fills 1/headroom.</summary>
        private void BarScales()
        {
            var refill = float.MaxValue;
            foreach (var chassis in _catalog.Chassis)
            {
                if (chassis == null || !_catalog.TryGetChassis(chassis.Id, out var spec)) continue;
                var modules = new ushort[chassis.Slots.Count];
                for (var i = 0; i < modules.Length && i < chassis.DefaultModules.Count; i++)
                    modules[i] = chassis.DefaultModules[i] != null ? chassis.DefaultModules[i].Id : LoadoutSelection.EmptySlot;
                var stats = LoadoutRules.Compute(spec, new LoadoutSelection(chassis.Id, modules), _catalog);
                var flight = chassis.Flight;
                _maxSpeed = Math.Max(_maxSpeed, FlightEstimate.TopSpeed(flight, stats) * BarHeadroom);
                _maxTurn = Math.Max(_maxTurn, FlightEstimate.TurnRate(flight, stats) * BarHeadroom);
                _maxClimb = Math.Max(_maxClimb, FlightEstimate.Climb(flight, stats) * BarHeadroom);
                _maxBoost = Math.Max(_maxBoost, FlightEstimate.BoostPower(flight, stats) * BarHeadroom);
                refill = Math.Min(refill, FlightEstimate.BoostRefill(flight, stats) / BarHeadroom);
            }
            _minRefill = refill < float.MaxValue ? refill : 1f;
        }

        /// <summary>The biggest hull any chassis can get (its base plus the best hull bonus per slot): the hull bar's full length.</summary>
        private float MaxHealth()
        {
            var best = 1f;
            foreach (var chassis in _catalog.Chassis)
            {
                if (chassis == null || !_catalog.TryGetChassis(chassis.Id, out var spec)) continue;
                var health = spec.BaseHealth;
                foreach (var type in spec.Slots)
                {
                    var bonus = 0f;
                    foreach (var module in _catalog.Modules)
                        if (module != null && module.Slot == type) bonus = Math.Max(bonus, module.Stats.HealthBonus);
                    health += bonus;
                }
                best = Math.Max(best, health);
            }
            return best;
        }
    }
}
