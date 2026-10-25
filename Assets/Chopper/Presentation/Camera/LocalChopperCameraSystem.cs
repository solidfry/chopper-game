using Chopper.Simulation.Flight;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Chopper.Presentation.Camera
{
    /// <summary>Pushes the local player's chopper pose to the scene's <see cref="ChopperCameraRig"/>.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class LocalChopperCameraSystem : SystemBase
    {
        private ChopperCameraRig _rig;

        protected override void OnCreate()
        {
            RequireForUpdate<NetworkStreamInGame>();
        }

        protected override void OnUpdate()
        {
            if (_rig == null)
            {
                _rig = Object.FindFirstObjectByType<ChopperCameraRig>();
                if (_rig == null) return;
            }

            // LocalToWorld here is the rendered pose: after transform update and physics graphical interpolation.
            foreach (var localToWorld in SystemAPI.Query<RefRO<LocalToWorld>>().WithAll<GhostOwnerIsLocal, ChopperFlightConfig>())
            {
                _rig.Follow(localToWorld.ValueRO.Position, localToWorld.ValueRO.Rotation, UnityEngine.Time.deltaTime);
                break;
            }
        }
    }
}
