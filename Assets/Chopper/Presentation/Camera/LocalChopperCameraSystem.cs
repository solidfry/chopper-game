using Chopper.Presentation.Bridge;
using Chopper.Simulation.Flight;
using Unity.Entities;
using Unity.NetCode;
using Unity.Physics;
using Unity.Transforms;

namespace Chopper.Presentation.Camera
{
    /// <summary>
    /// Pushes the local player's chopper pose and motion to the context's <see cref="ChopperCameraRig"/>. When a boost
    /// starts, the HUD glitches for a moment (the kick reaches the screen too).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class LocalChopperCameraSystem : PresentationSystemBase
    {
        private const float BoostGlitchSeconds = 0.18f;

        protected override void OnCreate()
        {
            base.OnCreate();
            RequireForUpdate<NetworkStreamInGame>();
        }

        protected override void OnUpdate()
        {
            if (!TryGetContext(out var context) || context.CameraRig == null) return;
            var rig = context.CameraRig;

            // LocalToWorld here is the rendered pose: after transform update and physics graphical interpolation.
            foreach (var (localToWorld, velocity, state) in SystemAPI
                         .Query<RefRO<LocalToWorld>, RefRO<PhysicsVelocity>, RefRO<ChopperFlightState>>()
                         .WithAll<GhostOwnerIsLocal, ChopperFlightConfig>())
            {
                rig.Follow(new CameraTarget
                {
                    Position = localToWorld.ValueRO.Position,
                    Rotation = localToWorld.ValueRO.Rotation,
                    Velocity = velocity.ValueRO.Linear,
                    Boosting = state.ValueRO.Boosting,
                }, UnityEngine.Time.deltaTime);

                var glitch = rig.ActiveSettings.BoostHudGlitch;
                if (rig.BoostKicked && glitch > 0f) context.HudLook.Disrupt(glitch, BoostGlitchSeconds);
                break;
            }
        }
    }
}
