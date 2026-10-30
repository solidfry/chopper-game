using System.Collections.Generic;
using Chopper.Simulation.Loadout;

namespace Chopper.Presentation.Visuals
{
    /// <summary>
    /// Chassis definitions by id, from the loadout catalog; built again when the catalog changes. Each system that needs
    /// presentation data of a chassis (cockpit model, eye) owns one: no shared state.
    /// </summary>
    public sealed class ChassisIndex
    {
        private readonly Dictionary<ushort, ChassisDefinition> _byId = new();
        private LoadoutCatalog _catalog;

        public bool TryGet(LoadoutCatalog catalog, ushort id, out ChassisDefinition chassis)
        {
            if (catalog != _catalog)
            {
                _byId.Clear();
                _catalog = catalog;
                if (catalog != null)
                    foreach (var entry in catalog.Chassis)
                        if (entry != null) _byId[entry.Id] = entry;
            }
            return _byId.TryGetValue(id, out chassis);
        }
    }
}
