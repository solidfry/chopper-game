using Chopper.Presentation.Bridge;
using Chopper.Presentation.Visuals;
using Chopper.Simulation.Combat;
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
    /// reaches the screen too). Kill cam: while you have no chopper, the rig chases your falling wreck
    /// (<see cref="Wreck.OwnerNetworkId"/>), then holds on the blast until the new chopper spawns.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class LocalChopperCameraSystem : PresentationSystemBase
    {
        private const float BoostGlitchSeconds = 0.18f;

        private readonly ChassisIndex _chassis = new();
        private MeasuredVelocity _velocity;
        private UnityEngine.Vector3 _wreckPoint;
        private bool _hasWreckPoint;

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
            var flying = false;
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
                // The optics of this chopper (none = no steps); a respawn with another loadout switches the zoom off.
                flying = true;
                context.Zoom.SetOptics(SystemAPI.HasComponent<OpticsConfig>(entity)
                    ? SystemAPI.GetComponent<OpticsConfig>(entity)
                    : default);
                rig.Follow(new CameraTarget
                {
                    Position = localToWorld.ValueRO.Position,
                    Rotation = localToWorld.ValueRO.Rotation,
                    Velocity = _velocity.Velocity,
                    Boosting = state.ValueRO.Boosting,
                    HasEye = hasEye,
                    Eye = hasEye ? chassis.CockpitEye : default,
                    Magnification = context.Zoom.Current,
                }, deltaTime);

                var glitch = rig.ActiveSettings.BoostHudGlitch;
                if (rig.BoostKicked && glitch > 0f) context.HudLook.Disrupt(glitch, BoostGlitchSeconds);
                break;
            }
            if (flying)
            {
                _hasWreckPoint = false;
                return;
            }
            context.Zoom.Clear();

            // Kill cam: your wreck while it falls; after the blast, hold on the last point.
            if (!SystemAPI.TryGetSingleton<NetworkId>(out var self)) return;
            foreach (var (wreck, localToWorld) in SystemAPI.Query<RefRO<Wreck>, RefRO<LocalToWorld>>())
            {
                if (!wreck.ValueRO.Active || wreck.ValueRO.OwnerNetworkId != self.Value) continue;
                _wreckPoint = localToWorld.ValueRO.Position;
                _hasWreckPoint = true;
                // Measured, like the live chopper: the wreck may be an interpolated ghost here.
                _velocity.Update(localToWorld.ValueRO.Position, deltaTime);
                rig.FollowWreck(_wreckPoint, _velocity.Velocity, deltaTime);
                return;
            }
            if (_hasWreckPoint) rig.FollowWreck(_wreckPoint, UnityEngine.Vector3.zero, deltaTime);
        }
    }
}
