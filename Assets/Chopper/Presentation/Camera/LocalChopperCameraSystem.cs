using Chopper.Presentation.Bridge;
using Chopper.Simulation.Flight;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;

namespace Chopper.Presentation.Camera
{
    /// <summary>Pushes the local player's chopper pose to the context's <see cref="ChopperCameraRig"/>.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class LocalChopperCameraSystem : PresentationSystemBase
    {
        protected override void OnCreate()
        {
            base.OnCreate();
            RequireForUpdate<NetworkStreamInGame>();
        }

        protected override void OnUpdate()
        {
            if (!TryGetContext(out var context) || context.CameraRig == null) return;

            // LocalToWorld here is the rendered pose: after transform update and physics graphical interpolation.
            foreach (var localToWorld in SystemAPI.Query<RefRO<LocalToWorld>>().WithAll<GhostOwnerIsLocal, ChopperFlightConfig>())
            {
                context.CameraRig.Follow(localToWorld.ValueRO.Position, localToWorld.ValueRO.Rotation, UnityEngine.Time.deltaTime);
                break;
            }
        }
    }
}
