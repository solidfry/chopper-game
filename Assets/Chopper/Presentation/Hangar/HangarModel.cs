using System;
using System.Collections.Generic;
using Chopper.Presentation.Visuals;
using Chopper.Simulation.Loadout;

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
        public readonly string Detail;
        public readonly bool Locked;
        public readonly bool Fitted;

        public HangarOptionRow(string name, string detail, bool locked, bool fitted)
        {
            Name = name;
            Detail = detail;
            Locked = locked;
            Fitted = fitted;
        }
    }

    /// <summary>A stat bar: now and, while a module is hovered, with that module fitted. Fractions 0..1.</summary>
    public readonly struct HangarStatRow
    {
        public readonly string Label;
        public readonly float Fraction;
        public readonly float PreviewFraction;
        public readonly string Text;

        public HangarStatRow(string label, float fraction, float previewFraction, string text)
        {
            Label = label;
            Fraction = fraction;
            PreviewFraction = previewFraction;
            Text = text;
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
    /// Each chassis remembers its own modules while you switch. <see cref="Changed"/>: the build, focus or skin changed
    /// (rebuild lists and the 3D view); <see cref="StatsChanged"/>: only the hover preview changed (stat bars).
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
        private readonly Dictionary<ushort, ModuleDefinition[]> _fitted = new();
        /// <summary>Choices for the focused slot; index 0 is "empty" (null).</summary>
        private readonly List<ModuleDefinition> _options = new();
        private readonly List<string> _chassisNames = new();
        private readonly List<string> _skinNames = new();
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

        public HangarModel(LoadoutCatalog catalog, SkinLibrary skins, LoadoutChoiceStore store, IPlayerUnlocks unlocks)
        {
            _catalog = catalog;
            _skins = skins;
            _store = store;
            _unlocks = unlocks ?? new AllUnlocked();

            foreach (var chassis in _catalog.Chassis) _chassisNames.Add(chassis != null ? chassis.DisplayName.ToUpperInvariant() : "?");
            if (_skins != null)
                foreach (var skin in _skins.Skins) _skinNames.Add(skin.DisplayName.ToUpperInvariant());
            _maxHealth = MaxHealth();
            BarScales();

            ChassisIndex = 0;
            if (_store != null && _store.TryLoad(out var saved, out var skinId)) Restore(saved, skinId);
            Refresh();
        }

        public event Action Changed;
        public event Action StatsChanged;
        /// <summary>A valid loadout was saved; the scene starts the match.</summary>
        public event Action Launched;
        /// <summary>A valid loadout was saved and the player asked for an online match; the scene runs the search.</summary>
        public event Action MatchRequested;
        /// <summary>The player cancelled the search.</summary>
        public event Action SearchCancelled;
        /// <summary><see cref="Searching"/> or <see cref="MatchStatus"/> changed.</summary>
        public event Action MatchStatusChanged;

        public IReadOnlyList<string> ChassisNames => _chassisNames;
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
        public float WeightFraction { get; private set; }
        public float PreviewWeightFraction { get; private set; }
        public string WeightText { get; private set; } = "";
        public bool OverWeight { get; private set; }
        public IReadOnlyList<string> SkinNames => _skinNames;
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

        /// <summary>Back out of a slot. False when already in the overview.</summary>
        public bool Back()
        {
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

        /// <summary>Picking a skin shows the paint (it cannot be seen on the wireframe).</summary>
        public void SelectSkin(int index)
        {
            if (index < 0 || index >= _skinNames.Count || index == SkinIndex) return;
            SkinIndex = index;
            PaintView = true;
            Refresh();
        }

        /// <summary>True: the 3D view shows the real paint and skin; false: the green wireframe.</summary>
        public bool PaintView { get; private set; }

        public void SetPaintView(bool paint)
        {
            if (paint == PaintView) return;
            PaintView = paint;
            Refresh();
        }

        /// <summary>Saves the loadout and raises <see cref="MatchRequested"/>. False when it is not valid or a search runs.</summary>
        public bool FindMatch()
        {
            if (!CanLaunch || Searching) return false;
            _store?.Save(Selection(FittedFor(Chassis)), SkinId);
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
            if (!CanLaunch || Searching) return false;
            _store?.Save(Selection(FittedFor(Chassis)), SkinId);
            Launched?.Invoke();
            return true;
        }

        private void Restore(in LoadoutSelection saved, ushort skinId)
        {
            for (var i = 0; i < _catalog.Chassis.Count; i++)
            {
                var chassis = _catalog.Chassis[i];
                if (chassis == null || chassis.Id != saved.ChassisId || saved.Modules.Count != chassis.Slots.Count) continue;
                var modules = new ModuleDefinition[chassis.Slots.Count];
                for (var slot = 0; slot < modules.Length; slot++) modules[slot] = Module(saved.Modules[slot]);
                _fitted[chassis.Id] = modules;
                ChassisIndex = i;
                break;
            }

            if (_skins == null) return;
            for (var i = 0; i < _skins.Skins.Count; i++)
                if (_skins.Skins[i].Id == skinId) SkinIndex = i;
        }

        private ModuleDefinition[] FittedFor(ChassisDefinition chassis)
        {
            if (_fitted.TryGetValue(chassis.Id, out var modules)) return modules;
            modules = new ModuleDefinition[chassis.Slots.Count];
            for (var slot = 0; slot < modules.Length && slot < chassis.DefaultModules.Count; slot++) modules[slot] = chassis.DefaultModules[slot];
            _fitted[chassis.Id] = modules;
            return modules;
        }

        private void Refresh()
        {
            _hovered = -1;
            BuildSlots();
            BuildOptions();
            RefreshStats();
            Changed?.Invoke();
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
            _optionRows.Add(new HangarOptionRow("EMPTY", "SAVES WEIGHT", false, current == null));

            var candidates = new List<(ModuleDefinition Module, ModuleSpec Spec)>();
            foreach (var module in _catalog.Modules)
            {
                if (module == null || module.Slot != type || !_catalog.TryGetModule(module.Id, out var spec) || !spec.FitsChassis(Chassis.Id)) continue;
                candidates.Add((module, spec));
            }
            candidates.Sort((a, b) => a.Spec.UnlockLevel != b.Spec.UnlockLevel
                ? a.Spec.UnlockLevel.CompareTo(b.Spec.UnlockLevel)
                : string.CompareOrdinal(a.Module.DisplayName, b.Module.DisplayName));

            foreach (var (module, spec) in candidates)
            {
                var locked = _unlocks.Level < spec.UnlockLevel || !_unlocks.Owns(module.Id);
                var detail = locked ? $"LEVEL {spec.UnlockLevel}" : $"{spec.Stats.Weight:0} KG";
                _options.Add(module);
                _optionRows.Add(new HangarOptionRow(module.DisplayName.ToUpperInvariant(), detail, locked, module == current));
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
            AddStat("HULL", now.Health / _maxHealth, preview.Health / _maxHealth, $"{preview.Health:0}");
            var speed = FlightEstimate.TopSpeed(flight, preview) * MetresPerSecondToKilometresPerHour;
            AddStat("SPEED", FlightEstimate.TopSpeed(flight, now) / _maxSpeed, FlightEstimate.TopSpeed(flight, preview) / _maxSpeed,
                $"{speed:0} KM/H");
            AddStat("TURN", FlightEstimate.TurnRate(flight, now) / _maxTurn, FlightEstimate.TurnRate(flight, preview) / _maxTurn,
                $"{FlightEstimate.TurnRate(flight, preview):0} DEG/S");
            AddStat("CLIMB", FlightEstimate.Climb(flight, now) / _maxClimb, FlightEstimate.Climb(flight, preview) / _maxClimb,
                $"{FlightEstimate.Climb(flight, preview):0.0} M/S2");
            AddStat("BOOST", FlightEstimate.BoostPower(flight, now) / _maxBoost, FlightEstimate.BoostPower(flight, preview) / _maxBoost,
                $"{FlightEstimate.BoostPower(flight, preview):0} M/S2");
            // Refill time: shorter is better, so the bar is the fastest refill over this one.
            AddStat("REFILL", _minRefill / FlightEstimate.BoostRefill(flight, now), _minRefill / FlightEstimate.BoostRefill(flight, preview),
                $"{FlightEstimate.BoostRefill(flight, preview):0.0} S");

            WeightFraction = now.WeightFraction;
            PreviewWeightFraction = preview.WeightFraction;
            WeightText = $"{preview.Weight:0} / {preview.WeightBudget:0} KG";
            OverWeight = now.Weight > now.WeightBudget;

            var result = LoadoutRules.Validate(selection, _catalog, _unlocks);
            Error = result.IsValid ? "" : Describe(result);
        }

        private void AddStat(string label, float now, float preview, string text) =>
            _statRows.Add(new HangarStatRow(label, Math.Clamp(now, 0f, 1f), Math.Clamp(preview, 0f, 1f), text));

        private string Describe(in LoadoutResult result)
        {
            var slot = result.Slot >= 0 && result.Slot < _slotRows.Count ? _slotRows[result.Slot].Label : "";
            return result.Error switch
            {
                LoadoutError.OverWeight => "OVER WEIGHT: REMOVE A MODULE",
                LoadoutError.ChassisLocked => "CHASSIS LOCKED",
                LoadoutError.ModuleLocked or LoadoutError.NotOwned => $"{slot}: MODULE LOCKED",
                LoadoutError.WrongSlotType or LoadoutError.WrongChassis => $"{slot}: MODULE DOES NOT FIT",
                _ => result.ToString().ToUpperInvariant(),
            };
        }

        private LoadoutSelection Selection(ModuleDefinition[] fitted)
        {
            var modules = new ushort[fitted.Length];
            for (var i = 0; i < modules.Length; i++) modules[i] = fitted[i] != null ? fitted[i].Id : LoadoutSelection.EmptySlot;
            return new LoadoutSelection(Chassis.Id, modules);
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
