using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;

namespace Chopper.Simulation.Flight
{
    /// <summary>
    /// The chopper's own lights (spot and nav lights on the prefab): on or off. The pilot toggles them with the Lights
    /// key (<see cref="ChopperInput.Lights"/>); the server decides and every client sees it (lights off = harder to
    /// spot at night). Presentation: <c>ChopperLightsSystem</c>.
    /// </summary>
    public struct ChopperLights : IComponentData
    {
        /// <summary>Starts on (baked as true).</summary>
        [GhostField] public bool On;
    }

    /// <summary>Server only: one Lights key press flips <see cref="ChopperLights.On"/>.</summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    public partial struct ChopperLightsToggleSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            foreach (var (input, lights) in SystemAPI.Query<RefRO<ChopperInput>, RefRW<ChopperLights>>().WithAll<Simulate>())
                if (input.ValueRO.Lights.IsSet)
                    lights.ValueRW.On = !lights.ValueRO.On;
        }
    }
}
