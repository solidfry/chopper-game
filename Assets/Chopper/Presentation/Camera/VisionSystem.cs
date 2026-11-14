using System.Collections.Generic;
using Chopper.Presentation.Bridge;
using Chopper.Simulation.Combat;
using Chopper.Simulation.Flight;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
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
        /// <summary>The flare mark sits this far under the chopper, metres.</summary>
        private const float FlareDrop = 6f;

        private readonly List<VisionGlow> _glows = new();
        private readonly List<HeatMark> _marks = new();
        /// <summary>Speed from the drawn movement: a remote chopper's physics velocity is not on every client.</summary>
        private readonly Dictionary<Entity, MeasuredVelocity> _speeds = new();
        private readonly HashSet<Entity> _seen = new();
        private readonly List<Entity> _gone = new();

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
            // Always read, so marks do not pile up while no view is on.
            _marks.Clear();
            var deltaTime = UnityEngine.Time.deltaTime;
            context.HeatMarks.Collect(_marks, deltaTime);
            if (!VisionMath.MarksTargets(mode)) _speeds.Clear();
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
                    _seen.Add(entity);
                    _speeds.TryGetValue(entity, out var measured);
                    measured.Update(pose.ValueRO.Position, deltaTime);
                    _speeds[entity] = measured;
                    var speed = measured.Speed;
                    var boosting = SystemAPI.HasComponent<ChopperFlightState>(entity) &&
                                   SystemAPI.GetComponent<ChopperFlightState>(entity).Boosting;
                    Add(mode, pose.ValueRO.Position, Seeker.Heat(targetable.ValueRO, speed, boosting), speed, eye, range);

                    // A burning flare: a second, hotter mark under the chopper (thermal sees the decoy too).
                    if (mode == VisionMode.Thermal && SystemAPI.HasComponent<CountermeasureState>(entity) &&
                        SystemAPI.GetComponent<CountermeasureState>(entity).DecoyRemaining > 0f)
                        Add(mode, pose.ValueRO.Position - pose.ValueRO.Up * FlareDrop, VisionMath.FlareHeat, speed, eye, range);
                }
                ForgetGone();

                foreach (var pose in SystemAPI.Query<RefRO<LocalToWorld>>().WithAll<GuidedMissile>())
                    Add(mode, pose.ValueRO.Position, VisionMath.MissileHeat, MissileSpeed, eye, range);

                // Rockets in flight and fresh blasts, from the effect systems. Deep scan marks targets only.
                if (mode != VisionMode.DeepScan)
                    foreach (var mark in _marks)
                        Add(mode, mark.Position, mark.Heat, mark.Speed, eye, range);
            }

            rig.ShowVision(mode, zoom.VisionBlend, _glows);
        }

        /// <summary>Drops the speed records of targets that are gone (dead, left).</summary>
        private void ForgetGone()
        {
            if (_speeds.Count == _seen.Count)
            {
                _seen.Clear();
                return;
            }
            _gone.Clear();
            foreach (var entity in _speeds.Keys)
                if (!_seen.Contains(entity)) _gone.Add(entity);
            foreach (var entity in _gone) _speeds.Remove(entity);
            _seen.Clear();
        }

        private void Add(VisionMode mode, float3 position, float heat, float speed, float3 eye, float range)
        {
            if (_glows.Count >= MaxGlows) return;
            var strength = VisionMath.Signal(mode, heat, speed, math.distance(eye, position), range);
            if (strength > 0f) _glows.Add(new VisionGlow { Position = position, Strength = strength });
        }
    }
}
