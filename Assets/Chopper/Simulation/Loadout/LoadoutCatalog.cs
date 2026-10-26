using System.Collections.Generic;
using UnityEngine;

namespace Chopper.Simulation.Loadout
{
    /// <summary>All chassis and modules in the game. One asset; the hangar and the server both read it.</summary>
    [CreateAssetMenu(menuName = "Chopper/Loadout/Catalog", fileName = "LoadoutCatalog")]
    public sealed class LoadoutCatalog : ScriptableObject, ILoadoutCatalog
    {
        [SerializeField] private ChassisDefinition[] chassis = System.Array.Empty<ChassisDefinition>();
        [SerializeField] private ModuleDefinition[] modules = System.Array.Empty<ModuleDefinition>();

        // Built on first use; assets do not change at runtime.
        private Dictionary<ushort, ChassisSpec> _chassis;
        private Dictionary<ushort, ModuleSpec> _modules;

        public IReadOnlyList<ChassisDefinition> Chassis => chassis;
        public IReadOnlyList<ModuleDefinition> Modules => modules;

        public bool TryGetChassis(ushort id, out ChassisSpec spec)
        {
            Build();
            return _chassis.TryGetValue(id, out spec);
        }

        public bool TryGetModule(ushort id, out ModuleSpec spec)
        {
            Build();
            return _modules.TryGetValue(id, out spec);
        }

        /// <summary>Editor only: used by the default catalog tool.</summary>
        public void Initialise(ChassisDefinition[] chassisList, ModuleDefinition[] moduleList)
        {
            chassis = chassisList;
            modules = moduleList;
            _chassis = null;
            _modules = null;
        }

        private void OnValidate()
        {
            _chassis = null;
            _modules = null;
            WarnDuplicates();
        }

        private void Build()
        {
            if (_chassis != null) return;
            _chassis = new Dictionary<ushort, ChassisSpec>();
            _modules = new Dictionary<ushort, ModuleSpec>();
            foreach (var definition in chassis)
                if (definition != null) _chassis[definition.Id] = definition.ToSpec();
            foreach (var definition in modules)
                if (definition != null) _modules[definition.Id] = definition.ToSpec();
        }

        private void WarnDuplicates()
        {
            var seen = new HashSet<ushort>();
            foreach (var definition in chassis)
                if (definition != null && (definition.Id == 0 || !seen.Add(definition.Id)))
                    Debug.LogWarning($"Chassis '{definition.name}' has id {definition.Id}: ids must be unique and not 0.", this);

            seen.Clear();
            foreach (var definition in modules)
                if (definition != null && (definition.Id == 0 || !seen.Add(definition.Id)))
                    Debug.LogWarning($"Module '{definition.name}' has id {definition.Id}: ids must be unique and not 0.", this);
        }
    }
}
