using Chopper.Presentation.Bridge;
using Chopper.Simulation.Flight;
using Unity.Entities;
using Unity.NetCode;
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

        private MeasuredVelocity _velocity;

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
            var deltaTime = UnityEngine.Time.deltaTime;
            foreach (var (localToWorld, state) in SystemAPI
                         .Query<RefRO<LocalToWorld>, RefRO<ChopperFlightState>>()
                         .WithAll<GhostOwnerIsLocal, ChopperFlightConfig>())
            {
                // Real movement, not the physics velocity: blocked (boosting into the ground), the view must not speed up.
                _velocity.Update(localToWorld.ValueRO.Position, deltaTime);
                rig.Follow(new CameraTarget
                {
                    Position = localToWorld.ValueRO.Position,
                    Rotation = localToWorld.ValueRO.Rotation,
                    Velocity = _velocity.Velocity,
                    Boosting = state.ValueRO.Boosting,
                }, deltaTime);

                var glitch = rig.ActiveSettings.BoostHudGlitch;
                if (rig.BoostKicked && glitch > 0f) context.HudLook.Disrupt(glitch, BoostGlitchSeconds);
                break;
            }
        }
    }
}
