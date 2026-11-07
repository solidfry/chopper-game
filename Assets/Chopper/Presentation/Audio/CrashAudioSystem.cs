using System;
using System.Collections.Generic;
using Chopper.Presentation.Bridge;
using Chopper.Presentation.Events;
using Chopper.Simulation.Combat;
using Chopper.Simulation.Visuals;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

namespace Chopper.Presentation.Audio
{
    /// <summary>
    /// Client only. Crash sounds for every chopper (and training target), heard by everyone near it: the server's crash
    /// and landing hits (<see cref="ChopperHit"/>, <see cref="DamageKind.Impact"/> and <see cref="DamageKind.Landing"/>)
    /// play the crash cue of what the chopper hit (<see cref="SoundLibrary.SurfaceCrash"/>). The surface comes from a
    /// short ray from the contact into the thing hit, in the client's own physics world (<see cref="Surfaces.Classify"/>),
    /// so the hit message stays as it is. Harder hits (more damage for the hull) play louder.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class CrashAudioSystem : PresentationSystemBase
    {
        /// <summary>A hit that takes this share of the hull (or more) plays at full volume.</summary>
        private const float FullVolumeShare = 0.2f;
        /// <summary>The softest crash still plays at this volume.</summary>
        private const float MinVolume = 0.35f;
        /// <summary>A hard landing is a thump, not a wreck: a little quieter.</summary>
        private const float LandingVolume = 0.8f;
        /// <summary>The ray starts this far off the hull (m) and reaches this far into what was hit.</summary>
        private const float RayBack = 1.5f;
        private const float RayIn = 3f;

        private readonly List<ChopperHit> _pending = new();
        private readonly Dictionary<int, Entity> _byGhostId = new();
        private PresentationContext _context;
        private IDisposable _subscription;
        private ComponentLookup<PhysicsSurface> _surfaces;

        protected override void OnCreate()
        {
            base.OnCreate();
            RequireForUpdate<NetworkStreamInGame>();
            _surfaces = GetComponentLookup<PhysicsSurface>(isReadOnly: true);
        }

        protected override void OnDestroy()
        {
            _subscription?.Dispose();
            _subscription = null;
        }

        protected override void OnUpdate()
        {
            if (!TryGetContext(out var context)) return;
            if (_context != context)
            {
                // New scene: a new message channel.
                _subscription?.Dispose();
                _pending.Clear();
                _context = context;
                _subscription = context.Events.Subscribe<ChopperHit>(OnHit);
            }
            if (_pending.Count == 0) return;

            var sounds = context.Sounds;
            if (sounds == null || !SystemAPI.TryGetSingleton<PhysicsWorldSingleton>(out var physics))
            {
                _pending.Clear();
                return;
            }
            var world = physics.CollisionWorld;
            _surfaces.Update(this);

            _byGhostId.Clear();
            foreach (var (ghost, entity) in SystemAPI.Query<RefRO<GhostInstance>>().WithAll<Health>().WithEntityAccess())
                _byGhostId[ghost.ValueRO.ghostId] = entity;

            foreach (var hit in _pending)
            {
                if (!_byGhostId.TryGetValue(hit.GhostId, out var entity) || !SystemAPI.HasComponent<LocalToWorld>(entity)) continue;
                var pose = SystemAPI.GetComponent<LocalToWorld>(entity).Value;
                var point = math.transform(pose, hit.LocalPoint);
                // The crash normal points away from what was hit (from the surface toward the chopper).
                var normal = math.normalizesafe(math.rotate(pose, hit.LocalNormal), math.up());
                var surface = SurfaceAt(world, entity, point, normal);

                var health = SystemAPI.GetComponent<Health>(entity);
                var volume = Volume(hit.Amount, health.Max) * (hit.Kind == DamageKind.Landing ? LandingVolume : 1f);
                context.Audio.Play(sounds.SurfaceCrash(surface), (Vector3)point, volume);
            }
            _pending.Clear();
        }

        private void OnHit(ChopperHit hit)
        {
            if (hit.Kind is DamageKind.Impact or DamageKind.Landing) _pending.Add(hit);
        }

        /// <summary>0..1: <see cref="MinVolume"/> for a scrape, full for a hit that takes <see cref="FullVolumeShare"/> of the hull.</summary>
        public static float Volume(float amount, float maxHealth)
        {
            var share = maxHealth > 0f ? amount / maxHealth : 1f;
            return math.lerp(MinVolume, 1f, math.saturate(share / FullVolumeShare));
        }

        /// <summary>What lies just past the contact, not the chopper itself. Nothing found (a glancing hit): concrete.</summary>
        private SurfaceKind SurfaceAt(in CollisionWorld world, Entity self, float3 point, float3 normal)
        {
            var ray = new RaycastInput
            {
                Start = point + normal * RayBack,
                End = point - normal * RayIn,
                Filter = CollisionFilter.Default,
            };
            var hits = new NativeList<Unity.Physics.RaycastHit>(8, Allocator.Temp);
            var best = 2f;
            var nearest = default(Unity.Physics.RaycastHit);
            if (world.CastRay(ray, ref hits))
            {
                foreach (var hit in hits)
                {
                    if (hit.Entity == self || hit.Fraction >= best) continue;
                    best = hit.Fraction;
                    nearest = hit;
                }
            }
            hits.Dispose();
            return best <= 1f ? Surfaces.Classify(world, nearest, _surfaces) : SurfaceKind.Concrete;
        }
    }
}
