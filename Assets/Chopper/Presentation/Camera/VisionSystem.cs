using System.Collections.Generic;
using Chopper.Presentation.Bridge;
using Chopper.Simulation.Combat;
using Chopper.Simulation.Flight;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Transforms;

namespace Chopper.Presentation.Camera
{
    /// <summary>
    /// Client only. While a special view of the optics is on (<see cref="ZoomState.ShownVision"/>), finds what it marks
    /// (<see cref="VisionMath.Signal"/>: live targets other than the player's own chopper, and missiles in flight) and
    /// gives the frame to the camera rig, which draws the picture (<see cref="VisionEffects"/>). After the camera
    /// system, so the glows use this frame's camera pose. The one place that reads ECS for the views.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(LocalChopperCameraSystem))]
    public partial class VisionSystem : PresentationSystemBase
    {
        /// <summary>Most marks per frame (the pool of glow quads stays small).</summary>
        private const int MaxGlows = 48;
        /// <summary>A missile moves at least this fast for the kinetic tracker (its velocity is not sent to every client).</summary>
        private const float MissileSpeed = 100f;

        private readonly List<VisionGlow> _glows = new();

        protected override void OnCreate()
        {
            base.OnCreate();
            RequireForUpdate<NetworkStreamInGame>();
        }

        protected override void OnUpdate()
        {
            if (!TryGetContext(out var context) || context.CameraRig == null) return;
            var rig = context.CameraRig;
            var zoom = context.Zoom;
            var mode = zoom.ShownVision;

            _glows.Clear();
            if (VisionMath.MarksTargets(mode))
            {
                float3 eye = rig.transform.position;
                var range = zoom.VisionRange;

                foreach (var (targetable, health, pose, entity) in SystemAPI
                             .Query<RefRO<Targetable>, RefRO<Health>, RefRO<LocalToWorld>>()
                             .WithNone<GhostOwnerIsLocal>()
                             .WithEntityAccess())
                {
                    if (health.ValueRO.IsDead) continue;
                    var speed = SystemAPI.HasComponent<PhysicsVelocity>(entity)
                        ? math.length(SystemAPI.GetComponent<PhysicsVelocity>(entity).Linear)
                        : 0f;
                    var boosting = SystemAPI.HasComponent<ChopperFlightState>(entity) &&
                                   SystemAPI.GetComponent<ChopperFlightState>(entity).Boosting;
                    Add(mode, pose.ValueRO.Position, Seeker.Heat(targetable.ValueRO, speed, boosting), speed, eye, range);
                }

                foreach (var pose in SystemAPI.Query<RefRO<LocalToWorld>>().WithAll<GuidedMissile>())
                    Add(mode, pose.ValueRO.Position, VisionMath.MissileHeat, MissileSpeed, eye, range);
            }

            rig.ShowVision(mode, zoom.VisionBlend, _glows);
        }

        private void Add(VisionMode mode, float3 position, float heat, float speed, float3 eye, float range)
        {
            if (_glows.Count >= MaxGlows) return;
            var strength = VisionMath.Signal(mode, heat, speed, math.distance(eye, position), range);
            if (strength > 0f) _glows.Add(new VisionGlow { Position = position, Strength = strength });
        }
    }
}
