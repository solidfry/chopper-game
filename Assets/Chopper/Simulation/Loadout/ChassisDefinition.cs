using UnityEngine;

namespace Chopper.Simulation.Loadout
{
    /// <summary>Design data for one chassis (Scout, Gunship, Assault, Lifter).</summary>
    [CreateAssetMenu(menuName = "Chopper/Loadout/Chassis", fileName = "Chassis")]
    public sealed class ChassisDefinition : ScriptableObject
    {
        [SerializeField, Tooltip("Unique, never 0, never reused. Saved in player loadouts.")]
        private ushort id;
        [SerializeField] private string displayName;
        [SerializeField, TextArea] private string description;
        [SerializeField] private float baseHealth = 100f;
        [SerializeField] private float weightBudget = 90f;
        [SerializeField] private int unlockLevel;
        [SerializeField, Tooltip("Slot order is the loadout order. Each slot needs a matching socket on the prefab.")]
        private SlotType[] slots = { SlotType.Nose, SlotType.Pylon, SlotType.Utility, SlotType.Rotor, SlotType.Engine, SlotType.Armour };
        [SerializeField, Tooltip("Chopper ghost prefab for this chassis (sockets named after the slots).")]
        private GameObject prefab;

        public ushort Id => id;
        public string DisplayName => displayName;
        public string Description => description;
        public GameObject Prefab => prefab;

        public ChassisSpec ToSpec() => new(id, displayName, baseHealth, weightBudget, slots, unlockLevel);

        /// <summary>Editor only: used by the default catalog tool.</summary>
        public void Initialise(ushort chassisId, string name, string text, float health, float budget, int level, SlotType[] slotLayout)
        {
            id = chassisId;
            displayName = name;
            description = text;
            baseHealth = health;
            weightBudget = budget;
            unlockLevel = level;
            slots = slotLayout;
        }
    }
}
