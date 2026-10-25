using Chopper.Simulation.Input;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Chopper.Simulation.Flight
{
    /// <summary>Reads the Flight action map into the local player's <see cref="ChopperInput"/> each tick.</summary>
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class ChopperInputSystem : SystemBase
    {
        private PlayerControls _controls;

        protected override void OnCreate()
        {
            _controls = new PlayerControls();
            _controls.Flight.Enable();
            RequireForUpdate<NetworkStreamInGame>();
        }

        protected override void OnDestroy()
        {
            _controls.Dispose();
        }

        protected override void OnUpdate()
        {
            var flight = _controls.Flight;
            var thrust = math.clamp(flight.Thrust.ReadValue<float>(), -1f, 1f);
            var yaw = math.clamp(flight.Yaw.ReadValue<float>(), -1f, 1f);
            var pitch = math.clamp(flight.Pitch.ReadValue<float>(), -1f, 1f);
            var roll = math.clamp(flight.Roll.ReadValue<float>(), -1f, 1f);
            var dash = flight.Dash.WasPressedThisFrame();

            foreach (var input in SystemAPI.Query<RefRW<ChopperInput>>().WithAll<GhostOwnerIsLocal>())
            {
                input.ValueRW = new ChopperInput { Thrust = thrust, Yaw = yaw, Pitch = pitch, Roll = roll };
                if (dash)
                    input.ValueRW.Dash.Set();
            }
        }
    }
}
