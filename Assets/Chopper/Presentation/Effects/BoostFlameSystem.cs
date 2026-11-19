using System.Collections.Generic;
using Chopper.Presentation.Bridge;
using Chopper.Simulation.Flight;
using Chopper.Simulation.Visuals;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Client only. Boost jet flames: while <see cref="ChopperFlightState.Boosting"/> is on, flames shoot back from the
    /// chopper's exhaust points (<see cref="ExhaustPoint"/>, baked from the Engine sockets). Works for the own predicted
    /// chopper and for interpolated ghosts, as Boosting is a ghost field. A burst of fat particles marks the boost start,
    /// the flame fades out fast when the boost ends. Emitters are pooled; one world-space particle system per exhaust
    /// point, moved to the chopper's pose every frame. Settings and material (the additive spark material) are on
    /// <see cref="CombatEffectsSettings"/>. In the cockpit view the own flames are scaled down so they do not fill the view.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class BoostFlameSystem : PresentationSystemBase
    {
        private readonly BoostFlames<Entity> _flames = new();
        private readonly List<(Vector3 Position, Vector3 Direction, float Size)> _exhausts = new();
        private PresentationContext _context;

        /// <summary>See <see cref="BoostFlames{TKey}.Follow"/>.</summary>
        public static float Follow(float current, float target, float rise, float fall, float deltaTime) =>
            BoostFlames<Entity>.Follow(current, target, rise, fall, deltaTime);

        protected override void OnCreate()
        {
            base.OnCreate();
            RequireForUpdate<NetworkStreamInGame>();
        }

        protected override void OnDestroy() => _flames.Clear();

        protected override void OnUpdate()
        {
            if (!TryGetContext(out var context) || context.Effects.Settings == null)
            {
                _flames.Clear();
                _context = null;
                return;
            }
            if (_context != context)
            {
                _flames.Clear();
                _context = context;
            }
            var settings = context.Effects.Settings;
            if (!BoostFlames<Entity>.Enabled(settings)) return;
            var deltaTime = SystemAPI.Time.DeltaTime;
            var cockpitView = context.CameraRig != null && context.CameraRig.HidesOwnChopper;

            _flames.Begin();
            foreach (var (pose, flight, exhaust, entity) in SystemAPI
                         .Query<RefRO<LocalToWorld>, RefRO<ChopperFlightState>, DynamicBuffer<ExhaustPoint>>()
                         .WithEntityAccess())
            {
                _exhausts.Clear();
                foreach (var point in exhaust)
                    _exhausts.Add(((Vector3)math.transform(pose.ValueRO.Value, point.LocalPosition),
                        (Vector3)math.rotate(pose.ValueRO.Value, point.LocalDirection), point.Size));
                var own = cockpitView && SystemAPI.HasComponent<GhostOwnerIsLocal>(entity) && SystemAPI.IsComponentEnabled<GhostOwnerIsLocal>(entity);
                _flames.Update(settings, entity, flight.ValueRO.Boosting, _exhausts, own ? settings.BoostFlameCockpitScale : 1f, deltaTime);
            }
            _flames.End(settings);
        }
    }
}
