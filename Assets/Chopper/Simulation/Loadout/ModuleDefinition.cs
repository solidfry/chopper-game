using UnityEngine;

namespace Chopper.Simulation.Loadout
{
    /// <summary>
    /// Design data for one module (rotor, engine, armour, weapon, utility). TODO: link weapon modules to their
    /// weapon tuning (hitscan / rocket configs) when the server spawns choppers from loadouts.
    /// </summary>
    [CreateAssetMenu(menuName = "Chopper/Loadout/Module", fileName = "Module")]
    public sealed class ModuleDefinition : ScriptableObject
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

        public ushort Id => id;
        public string DisplayName => displayName;
        public string Description => description;
        public SlotType Slot => slot;
        public GameObject Visual => visual;

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
