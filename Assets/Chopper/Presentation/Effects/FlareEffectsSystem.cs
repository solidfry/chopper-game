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
    /// Client only. A burst of falling flares each time a chopper drops them (<see cref="CountermeasureState.FlareCount"/>
    /// goes up), plus the flare sound. Cosmetic: the server decides what the flares do.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class FlareEffectsSystem : PresentationSystemBase
    {
        private const float FlareGravity = 3f;
        private const float FlareDrag = 0.8f;

        private sealed class Flare
        {
            public GameObject Visual;
            public Vector3 Position;
            public Vector3 Velocity;
            public float Remaining;
        }

        private readonly Dictionary<Entity, uint> _lastCount = new();
        private readonly Dictionary<Entity, uint> _seen = new();
        private readonly List<Flare> _flares = new();
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
                _flares.Clear();
                _effects = context.Effects;
            }
            var settings = _effects.Settings;
            if (settings == null) return;

            var deltaTime = UnityEngine.Time.deltaTime;
            MoveFlares(deltaTime);

            _seen.Clear();
            foreach (var (countermeasure, pose, entity) in SystemAPI
                         .Query<RefRO<CountermeasureState>, RefRO<LocalToWorld>>()
                         .WithEntityAccess())
            {
                var count = countermeasure.ValueRO.FlareCount;
                _seen[entity] = count;
                if (!_lastCount.TryGetValue(entity, out var last) || count <= last) continue;

                var velocity = SystemAPI.HasComponent<PhysicsVelocity>(entity)
                    ? (Vector3)SystemAPI.GetComponent<PhysicsVelocity>(entity).Linear
                    : Vector3.zero;
                Drop(settings, pose.ValueRO, velocity);
                if (context.Sounds != null) context.Audio.Play(context.Sounds.Flares, pose.ValueRO.Position);
            }

            _lastCount.Clear();
            foreach (var (entity, count) in _seen) _lastCount[entity] = count;
        }

        /// <summary>Flares fan out sideways and down from under the chopper, keeping some of its speed.</summary>
        private void Drop(CombatEffectsSettings settings, in LocalToWorld pose, Vector3 chopperVelocity)
        {
            if (settings.Flare == null) return;
            Vector3 right = pose.Right;
            Vector3 up = pose.Up;
            Vector3 back = -(Vector3)(float3)pose.Forward;
            Vector3 origin = pose.Position - pose.Up * 0.8f;

            for (var i = 0; i < settings.FlaresPerDrop; i++)
            {
                var side = i % 2 == 0 ? 1f : -1f;
                var velocity = chopperVelocity * 0.6f
                               + right * (side * Random.Range(6f, 14f))
                               - up * Random.Range(4f, 10f)
                               + back * Random.Range(2f, 6f);
                var visual = _effects.SpawnPooled(settings.Flare, origin, Quaternion.LookRotation(velocity.normalized));
                _flares.Add(new Flare { Visual = visual, Position = origin, Velocity = velocity, Remaining = settings.FlareLifetime });
            }
        }

        private void MoveFlares(float deltaTime)
        {
            var linger = _effects.Settings != null ? _effects.Settings.RocketTrailLinger : 1f;
            for (var i = _flares.Count - 1; i >= 0; i--)
            {
                var flare = _flares[i];
                flare.Velocity += Vector3.down * (FlareGravity * deltaTime);
                flare.Velocity *= Mathf.Exp(-FlareDrag * deltaTime);
                flare.Position += flare.Velocity * deltaTime;
                flare.Remaining -= deltaTime;
                if (flare.Visual != null) flare.Visual.transform.position = flare.Position;
                if (flare.Remaining > 0f) continue;

                _effects.Release(flare.Visual, linger);
                _flares.RemoveAt(i);
            }
        }
    }
}
