using Chopper.Simulation.Flight;
using Unity.Entities;
using UnityEngine;

namespace Chopper.Presentation.Visuals
{
    /// <summary>
    /// Client only. Shows <see cref="ChopperLights.On"/>: turns the baked Light companions of each chopper (spot and
    /// area lights in its <see cref="LinkedEntityGroup"/>) on or off. Writes only when the value changes.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class ChopperLightsSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            foreach (var (lights, group) in SystemAPI.Query<RefRO<ChopperLights>, DynamicBuffer<LinkedEntityGroup>>())
            {
                var on = lights.ValueRO.On;
                foreach (var linked in group)
                {
                    if (!EntityManager.HasComponent<CompanionComponent<Light>>(linked.Value)) continue;
                    Light light = EntityManager.GetComponentData<CompanionComponent<Light>>(linked.Value).CompanionRef.Value;
                    if (light != null && light.enabled != on) light.enabled = on;
                }
            }
        }
    }
}
