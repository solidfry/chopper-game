using Chopper.Presentation.Bridge;
using Chopper.Presentation.Visuals;
using Chopper.Simulation.Flight;
using Chopper.Simulation.Loadout;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;

namespace Chopper.Presentation.Camera
{
    /// <summary>
    /// Pushes the local player's chopper pose and motion to the context's <see cref="ChopperCameraRig"/>, with its
    /// chassis's cockpit eye (mounted views sit there). When a boost starts, the HUD glitches for a moment (the kick
    /// reaches the screen too).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class LocalChopperCameraSystem : PresentationSystemBase
    {
        private const float BoostGlitchSeconds = 0.18f;

        private readonly ChassisIndex _chassis = new();
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
            var catalog = context.Config != null ? context.Config.LoadoutCatalog : null;

            // LocalToWorld here is the rendered pose: after transform update and physics graphical interpolation.
            var deltaTime = UnityEngine.Time.deltaTime;
            foreach (var (localToWorld, state, entity) in SystemAPI
                         .Query<RefRO<LocalToWorld>, RefRO<ChopperFlightState>>()
                         .WithAll<GhostOwnerIsLocal, ChopperFlightConfig>()
                         .WithEntityAccess())
            {
                ChassisDefinition chassis = null;
                var hasEye = SystemAPI.HasComponent<ChopperLoadout>(entity) &&
                             _chassis.TryGet(catalog, SystemAPI.GetComponent<ChopperLoadout>(entity).ChassisId, out chassis) &&
                             chassis.Cockpit != null;

                // Real movement, not the physics velocity: blocked (boosting into the ground), the view must not speed up.
                _velocity.Update(localToWorld.ValueRO.Position, deltaTime);
                rig.Follow(new CameraTarget
                {
                    Position = localToWorld.ValueRO.Position,
                    Rotation = localToWorld.ValueRO.Rotation,
                    Velocity = _velocity.Velocity,
                    Boosting = state.ValueRO.Boosting,
                    HasEye = hasEye,
                    Eye = hasEye ? chassis.CockpitEye : default,
                }, deltaTime);

                var glitch = rig.ActiveSettings.BoostHudGlitch;
                if (rig.BoostKicked && glitch > 0f) context.HudLook.Disrupt(glitch, BoostGlitchSeconds);
                break;
            }
        }
    }
}
