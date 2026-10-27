using System.Collections.Generic;
using Chopper.Simulation.Config;
using Chopper.Simulation.Definitions;
using UnityEngine;

namespace Chopper.Simulation.Loadout
{
    /// <summary>
    /// Design data for one module (rotor, engine, armour, weapon, utility). Weapon modules point to their weapon
    /// tuning (<see cref="WeaponDefinition"/>).
    /// </summary>
    [CreateAssetMenu(menuName = "Chopper/Loadout/Module", fileName = "Module")]
    public sealed class ModuleDefinition : ScriptableObject, IValidatedAsset
    {
        [SerializeField, Tooltip("Unique, never 0, never reused. Saved in player loadouts.")]
        private ushort id;
        [SerializeField] private string displayName;
        [SerializeField, TextArea] private string description;
        [SerializeField] private SlotType slot;
        [SerializeField] private ModuleStats stats = ModuleStats.None;
        [SerializeField] private int unlockLevel;
        [SerializeField, Tooltip("Empty = fits every chassis.")]
        private ChassisDefinition[] allowedChassis = System.Array.Empty<ChassisDefinition>();
        [SerializeField, Tooltip("Mesh shown on the chassis socket (hangar and in game).")]
        private GameObject visual;
        [SerializeField, Tooltip("Nose: a hitscan weapon. Pylon: a rocket or guided missile. Empty for non-weapon modules.")]
        private WeaponDefinition weapon;
        [SerializeField, Tooltip("Utility behaviour (flares, later EMP, shield...). Empty for most modules.")]
        private ModuleAbilityDefinition ability;

        public ushort Id => id;
        public string DisplayName => displayName;
        public string Description => description;
        public SlotType Slot => slot;
        public GameObject Visual => visual;
        public WeaponDefinition Weapon => weapon;
        public ModuleAbilityDefinition Ability => ability;
        public ModuleStats Stats => stats;

        public void CollectProblems(List<string> problems)
        {
            if (id == 0) problems.Add($"{name}: id must not be 0.");
            if (string.IsNullOrWhiteSpace(displayName)) problems.Add($"{name}: no display name.");
            if (stats.Weight < 0f) problems.Add($"{name}: weight must not be below 0.");
            if (weapon is HitscanWeaponDefinition && slot != SlotType.Nose)
                problems.Add($"{name}: a hitscan weapon must be in the Nose slot.");
            if ((weapon is RocketWeaponDefinition || weapon is GuidedMissileDefinition) && slot != SlotType.Pylon)
                problems.Add($"{name}: rockets and missiles must be in a Pylon slot.");
        }

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetWeapon(WeaponDefinition definition) => weapon = definition;

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetVisual(GameObject prefab) => visual = prefab;

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetAbility(ModuleAbilityDefinition definition) => ability = definition;

        public ModuleSpec ToSpec()
        {
            var chassisIds = new ushort[allowedChassis.Length];
            for (var i = 0; i < allowedChassis.Length; i++)
                chassisIds[i] = allowedChassis[i] != null ? allowedChassis[i].Id : (ushort)0;
            return new ModuleSpec(id, displayName, slot, stats, unlockLevel, chassisIds);
        }

        /// <summary>Editor only: used by the default catalog tool.</summary>
        public void Initialise(ushort moduleId, string name, string text, SlotType slotType, ModuleStats moduleStats, int level,
            ChassisDefinition[] chassis = null)
        {
            id = moduleId;
            displayName = name;
            description = text;
            slot = slotType;
            stats = moduleStats;
            unlockLevel = level;
            allowedChassis = chassis ?? System.Array.Empty<ChassisDefinition>();
        }
    }
}
