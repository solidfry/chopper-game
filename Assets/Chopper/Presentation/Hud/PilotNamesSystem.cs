using System.Collections.Generic;
using Chopper.Presentation.Bridge;
using Chopper.Simulation.Bots;
using Chopper.Simulation.Loadout;
using Chopper.Simulation.Session;
using Unity.Entities;
using Unity.NetCode;

namespace Chopper.Presentation.Hud
{
    /// <summary>
    /// Client only. Each player's pilot call sign and chassis name (from their chopper's ghost loadout and the catalog)
    /// (a server bot: its roster name, <see cref="BotSettings.NameOf"/>)
    /// for the kill feed, the leader line, the scoreboard and the local player's own call sign
    /// (<see cref="MatchHudModel.SetCallSign"/>, <see cref="MatchHudModel.SetChassis"/>). Only changes are passed on.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class PilotNamesSystem : PresentationSystemBase
    {
        private readonly Dictionary<int, (ushort Pilot, ushort Chassis)> _shown = new();
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
                var now = (loadout.ValueRO.PilotId, loadout.ValueRO.ChassisId);
                if (_shown.TryGetValue(networkId, out var shown) && shown == now) continue;
                _shown[networkId] = now;
                // A server bot: its name comes from the bot settings (the same asset the server baked), by id.
                var bots = context.Config != null ? context.Config.Bots : null;
                if (BotIds.IsBot(networkId) && bots != null) _model.SetPlayerName(networkId, bots.NameOf(networkId));
                var pilot = catalog.FindPilot(now.PilotId);
                _model.SetCallSign(networkId, pilot != null ? pilot.CallSign : "");
                var chassis = catalog.TryGetChassis(now.ChassisId, out var spec) ? spec.Name ?? "" : "";
                _model.SetChassis(networkId, chassis.ToUpperInvariant());
            }
        }
    }
}
