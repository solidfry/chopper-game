using System.Collections.Generic;
using Chopper.Presentation.Bridge;
using Chopper.Simulation.Loadout;
using Chopper.Simulation.Session;
using Unity.Entities;
using Unity.NetCode;

namespace Chopper.Presentation.Hud
{
    /// <summary>
    /// Client only. Each player's pilot call sign (from their chopper's ghost loadout and the catalog) for the kill feed,
    /// the leader line and the local player's own call sign (<see cref="MatchHudModel.SetCallSign"/>). Only changes are
    /// passed on.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class PilotNamesSystem : PresentationSystemBase
    {
        private readonly Dictionary<int, ushort> _shown = new();
        private MatchHudModel _model;

        protected override void OnCreate()
        {
            base.OnCreate();
            RequireForUpdate<NetworkStreamInGame>();
            RequireForUpdate<LoadoutCatalogRef>();
        }

        protected override void OnUpdate()
        {
            if (!TryGetContext(out var context)) return;
            // New scene: a new model knows no call signs yet.
            if (_model != context.Match)
            {
                _model = context.Match;
                _shown.Clear();
            }
            var catalog = SystemAPI.GetSingleton<LoadoutCatalogRef>().Catalog.Value;
            if (catalog == null) return;

            foreach (var (loadout, owner) in SystemAPI.Query<RefRO<ChopperLoadout>, RefRO<GhostOwner>>())
            {
                var networkId = owner.ValueRO.NetworkId;
                var pilotId = loadout.ValueRO.PilotId;
                if (_shown.TryGetValue(networkId, out var shown) && shown == pilotId) continue;
                _shown[networkId] = pilotId;
                var pilot = catalog.FindPilot(pilotId);
                _model.SetCallSign(networkId, pilot != null ? pilot.CallSign : "");
            }
        }
    }
}
