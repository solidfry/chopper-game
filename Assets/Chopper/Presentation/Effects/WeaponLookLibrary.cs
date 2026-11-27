using System;
using System.Collections.Generic;
using Chopper.Simulation.Config;
using Chopper.Simulation.Loadout;
using UnityEngine;

namespace Chopper.Presentation.Effects
{
    /// <summary>Which default look a weapon falls back to when its module has no family.</summary>
    public enum WeaponKind : byte
    {
        Gun = 0,
        Rocket = 1,
        /// <summary>A rocket with gravity (falls).</summary>
        Bomb = 2,
        /// <summary>A rocket with drag (slows and drifts).</summary>
        Mine = 3,
        Guided = 4,
    }

    /// <summary>
    /// Module id -> <see cref="WeaponLook"/> (Docs/WeaponFeelPlan.md). Each family lists its modules; a module with no
    /// family (or id 0: turrets, cluster bomblets) gets its kind's default. Linked from
    /// <see cref="CombatEffectsSettings.WeaponLooks"/>; empty = every weapon uses the shared prefabs and cues.
    /// </summary>
    [CreateAssetMenu(menuName = "Chopper/Effects/Weapon Look Library", fileName = "WeaponLookLibrary")]
    public sealed class WeaponLookLibrary : ScriptableObject, IValidatedAsset
    {
        [Serializable]
        public sealed class Family
        {
            [SerializeField] private string name;
            [SerializeField] private WeaponLook look;
            [SerializeField, Tooltip("Modules that use this look (a module and its Mk tiers).")]
            private List<ModuleDefinition> modules = new();

            public string Name => name;
            public WeaponLook Look => look;
            public IReadOnlyList<ModuleDefinition> Modules => modules;

            public Family() { }

            public Family(string familyName, WeaponLook familyLook, IEnumerable<ModuleDefinition> members)
            {
                name = familyName;
                look = familyLook;
                modules = new List<ModuleDefinition>(members);
            }
        }

        [Header("Kind defaults (a module with no family)")]
        [SerializeField] private WeaponLook gun;
        [SerializeField] private WeaponLook rocket;
        [SerializeField, Tooltip("Empty = the rocket default.")] private WeaponLook bomb;
        [SerializeField, Tooltip("Empty = the rocket default.")] private WeaponLook mine;
        [SerializeField] private WeaponLook guided;

        [SerializeField] private List<Family> families = new();

        [NonSerialized] private Dictionary<ushort, WeaponLook> _byModule;

        public IReadOnlyList<Family> Families => families;

        /// <summary>Kind of a rocket-type round from its path (same numbers the server flies).</summary>
        public static WeaponKind RocketKind(float gravity, float drag) =>
            gravity > 0f ? WeaponKind.Bomb : drag > 0f ? WeaponKind.Mine : WeaponKind.Rocket;

        /// <summary>The look for <paramref name="moduleId"/>: its family, else the kind default. May be null.</summary>
        public WeaponLook For(ushort moduleId, WeaponKind kind)
        {
            if (moduleId != 0 && Index().TryGetValue(moduleId, out var look) && look != null) return look;
            return Default(kind);
        }

        public WeaponLook Default(WeaponKind kind) => kind switch
        {
            WeaponKind.Gun => gun,
            WeaponKind.Bomb => bomb != null ? bomb : rocket,
            WeaponKind.Mine => mine != null ? mine : rocket,
            WeaponKind.Guided => guided,
            _ => rocket,
        };

        private Dictionary<ushort, WeaponLook> Index()
        {
            if (_byModule != null) return _byModule;
            _byModule = new Dictionary<ushort, WeaponLook>();
            foreach (var family in families)
            {
                if (family == null) continue;
                foreach (var module in family.Modules)
                    if (module != null && !_byModule.ContainsKey(module.Id))
                        _byModule.Add(module.Id, family.Look);
            }
            return _byModule;
        }

        private void OnValidate() => _byModule = null;

        public void CollectProblems(List<string> problems)
        {
            if (gun == null) problems.Add($"{name}: no gun default look.");
            if (rocket == null) problems.Add($"{name}: no rocket default look.");
            if (guided == null) problems.Add($"{name}: no guided missile default look.");
            var seen = new Dictionary<ushort, string>();
            for (var i = 0; i < families.Count; i++)
            {
                var family = families[i];
                if (family == null) continue;
                var label = string.IsNullOrEmpty(family.Name) ? $"family {i + 1}" : family.Name;
                if (family.Look == null) problems.Add($"{name}: {label} has no look.");
                foreach (var module in family.Modules)
                {
                    if (module == null)
                    {
                        problems.Add($"{name}: {label} has an empty module slot.");
                        continue;
                    }
                    if (module.Weapon == null) problems.Add($"{name}: {label} lists {module.name}, which has no weapon.");
                    if (seen.TryGetValue(module.Id, out var other))
                        problems.Add($"{name}: {module.name} is in {other} and {label}.");
                    else seen.Add(module.Id, label);
                }
                family.Look?.CollectProblems(problems);
            }
        }

        /// <summary>Editor only: used by the setup tool and tests.</summary>
        public void Initialise(WeaponLook gunLook, WeaponLook rocketLook, WeaponLook guidedLook, WeaponLook bombLook = null,
            WeaponLook mineLook = null)
        {
            gun = gunLook;
            rocket = rocketLook;
            guided = guidedLook;
            bomb = bombLook;
            mine = mineLook;
            _byModule = null;
        }

        /// <summary>Editor only: fills empty kind defaults (setup repair). True if one was filled.</summary>
        public bool FillDefaults(WeaponLook gunLook, WeaponLook rocketLook, WeaponLook guidedLook)
        {
            var filled = false;
            if (gun == null) { gun = gunLook; filled = true; }
            if (rocket == null) { rocket = rocketLook; filled = true; }
            if (guided == null) { guided = guidedLook; filled = true; }
            return filled;
        }

        /// <summary>Editor only: used by the setup tool and tests.</summary>
        public void SetFamilies(IEnumerable<Family> list)
        {
            families = new List<Family>(list);
            _byModule = null;
        }
    }
}
