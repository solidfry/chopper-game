using System.Collections.Generic;
using Chopper.Presentation.Bridge;
using Chopper.Simulation.Combat;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Client only. Each time a chopper drops flares (<see cref="CountermeasureState.FlareCount"/> goes up) they fire out
    /// one by one, left and right in turn (in time with the pops of the flare sound), then hang, fizz and burn out in the
    /// sky. Cosmetic: the server decides what the flares do.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class FlareEffectsSystem : PresentationSystemBase
    {
        private readonly Dictionary<Entity, uint> _lastCount = new();
        private readonly Dictionary<Entity, uint> _seen = new();
        private readonly FlareVolleys<Entity> _volleys = new();
        private ComponentLookup<LocalToWorld> _poses;
        private ComponentLookup<ObservedVelocity> _observed;
        private ComponentLookup<PhysicsVelocity> _physics;
        private CombatEffects _effects;

        protected override void OnCreate()
        {
            base.OnCreate();
            RequireForUpdate<NetworkStreamInGame>();
        }

        protected override void OnUpdate()
        {
            if (!TryGetContext(out var context)) return;
            if (_effects != context.Effects)
            {
                _volleys.Clear();
                _effects = context.Effects;
            }
            var settings = _effects.Settings;
            if (settings == null) return;

            var deltaTime = UnityEngine.Time.deltaTime;
            _poses = SystemAPI.GetComponentLookup<LocalToWorld>(true);
            _observed = SystemAPI.GetComponentLookup<ObservedVelocity>(true);
            _physics = SystemAPI.GetComponentLookup<PhysicsVelocity>(true);
            _volleys.Tick(_effects, deltaTime, PoseOf);

            _seen.Clear();
            foreach (var (countermeasure, pose, entity) in SystemAPI
                         .Query<RefRO<CountermeasureState>, RefRO<LocalToWorld>>()
                         .WithEntityAccess())
            {
                var count = countermeasure.ValueRO.FlareCount;
                _seen[entity] = count;
                if (!_lastCount.TryGetValue(entity, out var last) || count <= last) continue;

                var burn = countermeasure.ValueRO.FlareRemaining > 0f ? countermeasure.ValueRO.FlareRemaining : settings.FlareLifetime;
                _volleys.Drop(entity, settings.FlaresPerDrop, burn);
                if (context.Sounds != null) context.Audio.Play(context.Sounds.Flares, pose.ValueRO.Position);
            }

            _lastCount.Clear();
            foreach (var (entity, count) in _seen) _lastCount[entity] = count;
        }

        private bool PoseOf(Entity chopper, out Matrix4x4 pose, out Vector3 velocity)
        {
            pose = default;
            velocity = default;
            if (!_poses.HasComponent(chopper)) return false;
            pose = (Matrix4x4)_poses[chopper].Value;
            velocity = ObservedVelocity.Of(chopper, _observed, _physics);
            return true;
        }
    }
}
