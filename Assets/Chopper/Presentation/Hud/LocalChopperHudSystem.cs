using Chopper.Simulation.Flight;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using UnityEngine;

namespace Chopper.Presentation.Hud
{
    /// <summary>Writes the local player's chopper values (speed, altitude, dash) into the scene's HUD model.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class LocalChopperHudSystem : SystemBase
    {
        private ChopperHudModelHost _host;

        protected override void OnCreate()
        {
            RequireForUpdate<NetworkStreamInGame>();
        }

        protected override void OnUpdate()
        {
            if (_host == null)
            {
                _host = Object.FindFirstObjectByType<ChopperHudModelHost>();
                if (_host == null) return;
            }

            foreach (var (velocity, state, config) in SystemAPI
                         .Query<RefRO<PhysicsVelocity>, RefRO<ChopperFlightState>, RefRO<ChopperFlightConfig>>()
                         .WithAll<GhostOwnerIsLocal>())
            {
                _host.Model.Update(
                    math.length(velocity.ValueRO.Linear),
                    state.ValueRO.Altitude,
                    state.ValueRO.DashCooldownRemaining,
                    config.ValueRO.DashCooldown);
                return;
            }

            _host.Model.ClearChopper();
        }
    }
}
