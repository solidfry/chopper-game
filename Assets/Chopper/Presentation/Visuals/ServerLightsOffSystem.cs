using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Chopper.Presentation.Visuals
{
    /// <summary>On a light that <see cref="ServerLightsOffSystem"/> has turned off.</summary>
    public struct ServerLightOff : IComponentData
    {
    }

    /// <summary>
    /// Server world only. The server draws nothing, but a server world in the same process (training, local sessions, the
    /// editor) still gets a real Light for each baked one. They add to the client's lights: the server's copy of your
    /// chopper is behind the predicted one, more so at boost speed, so its nose lights shone inside your cockpit (green,
    /// red and white flashes, owner 7 Nov). The city's lights also counted twice against the Forward+ limit.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class ServerLightsOffSystem : SystemBase
    {
        private EntityQuery _lights;

        protected override void OnCreate()
        {
            _lights = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<CompanionComponent<Light>>() },
                None = new[] { ComponentType.ReadOnly<ServerLightOff>() },
            });
            RequireForUpdate(_lights);
        }

        protected override void OnUpdate()
        {
            using var entities = _lights.ToEntityArray(Allocator.Temp);
            foreach (var entity in entities)
            {
                Light light = EntityManager.GetComponentData<CompanionComponent<Light>>(entity).CompanionRef.Value;
                if (light != null) light.enabled = false;
                EntityManager.AddComponent<ServerLightOff>(entity);
            }
        }
    }
}
