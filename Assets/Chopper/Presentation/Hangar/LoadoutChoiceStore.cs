using System;
using System.Collections.Generic;
using System.Text;
using Chopper.Simulation.Loadout;
using Chopper.Simulation.Session;
using Unity.Entities;
using UnityEngine;

namespace Chopper.Presentation.Hangar
{
    /// <summary>Key-value storage for the hangar's saves. PlayerPrefs in the game; a dictionary in tests.</summary>
    public interface IHangarPrefs
    {
        string GetString(string key, string fallback);
        int GetInt(string key, int fallback);
        void SetString(string key, string value);
        void SetInt(string key, int value);
        void Save();
    }

    public sealed class PlayerPrefsHangarPrefs : IHangarPrefs
    {
        public string GetString(string key, string fallback) => PlayerPrefs.GetString(key, fallback);
        public int GetInt(string key, int fallback) => PlayerPrefs.GetInt(key, fallback);
        public void SetString(string key, string value) => PlayerPrefs.SetString(key, value);
        public void SetInt(string key, int value) => PlayerPrefs.SetInt(key, value);
        public void Save() => PlayerPrefs.Save();
    }

    /// <summary>A named build (preset) of one chassis: one module id per slot (0 = empty).</summary>
    public readonly struct LoadoutPreset
    {
        public readonly string Name;
        public readonly ushort[] Modules;

        public LoadoutPreset(string name, ushort[] modules)
        {
            Name = name;
            Modules = modules;
        }
    }

    /// <summary>
    /// The hangar's saves (PlayerPrefs now, Cloud Save later, Phase 5):
    /// <list type="bullet">
    /// <item>the choice for the next spawn (loadout + skin), read by the client world when it starts;</item>
    /// <item>the build of each chassis, so every chassis keeps its modules across sessions;</item>
    /// <item>named builds (presets) per chassis.</item>
    /// </list>
    /// Text form of a loadout: <c>chassis:module,module,...</c> (0 = empty slot). The server validates whatever arrives.
    /// </summary>
    public sealed class LoadoutChoiceStore
    {
        private const string LoadoutKey = "Chopper.Hangar.Loadout";
        private const string SkinKey = "Chopper.Hangar.Skin";
        private const string BuildKey = "Chopper.Hangar.Build.";
        private const string PresetsKey = "Chopper.Hangar.Presets.";
        private const string TrainingMapKey = "Chopper.Hangar.TrainingMap";
        /// <summary>Longest preset name kept.</summary>
        public const int MaxPresetName = 24;

        private readonly IHangarPrefs _prefs;

        public LoadoutChoiceStore(IHangarPrefs prefs = null) => _prefs = prefs ?? new PlayerPrefsHangarPrefs();

        public bool TryLoad(out LoadoutSelection selection, out ushort skinId)
        {
            skinId = (ushort)Mathf.Clamp(_prefs.GetInt(SkinKey, 0), 0, ushort.MaxValue);
            return TryDecode(_prefs.GetString(LoadoutKey, ""), out selection);
        }

        public void Save(in LoadoutSelection selection, ushort skinId)
        {
            _prefs.SetString(LoadoutKey, Encode(selection));
            _prefs.SetInt(SkinKey, skinId);
            SaveBuild(selection);
        }

        /// <summary>The scene of the last training map picked ("" = none yet).</summary>
        public string TrainingMap
        {
            get => _prefs.GetString(TrainingMapKey, "");
            set
            {
                _prefs.SetString(TrainingMapKey, value ?? "");
                _prefs.Save();
            }
        }

        /// <summary>The last build of one chassis (false when none was saved).</summary>
        public bool TryLoadBuild(ushort chassisId, out LoadoutSelection selection) =>
            TryDecode(_prefs.GetString(BuildKey + chassisId, ""), out selection) && selection.ChassisId == chassisId;

        public void SaveBuild(in LoadoutSelection selection)
        {
            _prefs.SetString(BuildKey + selection.ChassisId, Encode(selection));
            _prefs.Save();
        }

        public List<LoadoutPreset> LoadPresets(ushort chassisId) => DecodePresets(_prefs.GetString(PresetsKey + chassisId, ""));

        public void SavePresets(ushort chassisId, IReadOnlyList<LoadoutPreset> presets)
        {
            _prefs.SetString(PresetsKey + chassisId, EncodePresets(presets));
            _prefs.Save();
        }

        public static string Encode(in LoadoutSelection selection)
        {
            var text = new StringBuilder();
            text.Append(selection.ChassisId).Append(':');
            AppendModules(text, selection.Modules);
            return text.ToString();
        }

        /// <summary>False for empty or broken text, chassis 0, or more slots than a <see cref="LoadoutCode"/> holds.</summary>
        public static bool TryDecode(string text, out LoadoutSelection selection)
        {
            selection = default;
            if (string.IsNullOrEmpty(text)) return false;
            var parts = text.Split(':');
            if (parts.Length != 2 || !ushort.TryParse(parts[0], out var chassisId) || chassisId == 0) return false;
            if (!TryDecodeModules(parts[1], out var modules)) return false;
            selection = new LoadoutSelection(chassisId, modules);
            return true;
        }

        /// <summary>Upper case, no line breaks or tabs (they separate the saved text), at most <see cref="MaxPresetName"/> characters.</summary>
        public static string CleanName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            var text = new StringBuilder();
            foreach (var c in name.Trim())
            {
                if (char.IsControl(c)) continue;
                text.Append(char.ToUpperInvariant(c));
                if (text.Length == MaxPresetName) break;
            }
            return text.ToString().Trim();
        }

        /// <summary>One preset per line: <c>name TAB module,module,...</c>.</summary>
        public static string EncodePresets(IReadOnlyList<LoadoutPreset> presets)
        {
            var text = new StringBuilder();
            foreach (var preset in presets)
            {
                var name = CleanName(preset.Name);
                if (name.Length == 0 || preset.Modules == null) continue;
                if (text.Length > 0) text.Append('\n');
                text.Append(name).Append('\t');
                AppendModules(text, preset.Modules);
            }
            return text.ToString();
        }

        /// <summary>Skips broken lines.</summary>
        public static List<LoadoutPreset> DecodePresets(string text)
        {
            var presets = new List<LoadoutPreset>();
            if (string.IsNullOrEmpty(text)) return presets;
            foreach (var line in text.Split('\n'))
            {
                var parts = line.Split('\t');
                if (parts.Length != 2) continue;
                var name = CleanName(parts[0]);
                if (name.Length == 0 || !TryDecodeModules(parts[1], out var modules)) continue;
                presets.Add(new LoadoutPreset(name, modules));
            }
            return presets;
        }

        private static void AppendModules(StringBuilder text, IReadOnlyList<ushort> modules)
        {
            for (var i = 0; i < modules.Count; i++)
            {
                if (i > 0) text.Append(',');
                text.Append(modules[i]);
            }
        }

        private static bool TryDecodeModules(string text, out ushort[] modules)
        {
            var pieces = text.Length == 0 ? Array.Empty<string>() : text.Split(',');
            modules = null;
            if (pieces.Length > LoadoutCode.MaxSlots) return false;
            var result = new ushort[pieces.Length];
            for (var i = 0; i < pieces.Length; i++)
                if (!ushort.TryParse(pieces[i], out result[i])) return false;
            modules = result;
            return true;
        }
    }

    /// <summary>
    /// Client only (not thin clients: they pick their own). When the client world starts, puts the saved hangar choice
    /// in the <see cref="LoadoutChoice"/> singleton, which <see cref="GoInGameClientSystem"/> sends with the join request.
    /// No saved choice: no singleton, so the server gives the default chopper.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class LoadoutChoiceSystem : SystemBase
    {
        protected override void OnCreate()
        {
            if (!new LoadoutChoiceStore().TryLoad(out var selection, out var skinId)) return;
            EntityManager.CreateSingleton(new LoadoutChoice { Code = LoadoutCode.From(selection), SkinId = skinId }, "LoadoutChoice");
        }

        protected override void OnUpdate() => Enabled = false;
    }
}
