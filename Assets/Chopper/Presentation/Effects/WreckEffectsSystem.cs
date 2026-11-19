using System;
using System.Collections.Generic;
using Chopper.Presentation.Bridge;
using Chopper.Simulation.Combat;
using Chopper.Presentation.Events;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Physics;
using UnityEngine;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Client only. A wreck's explosion where the server says it blew up (<see cref="WreckExploded"/>,
    /// Docs/ChopperDeathDesign.md): the layered blast over time (<see cref="BlastSequence"/>, traced against the ECS
    /// static world), a heat mark, and camera shake that falls off with distance. The falling smoke and fire come from
    /// <see cref="DamageEffectsSystem"/>. The break-up: <see cref="WreckDebris"/>.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class WreckEffectsSystem : PresentationSystemBase
    {
        private readonly List<WreckExploded> _explosions = new();
        private readonly BlastSequence _sequence = new();
        private PresentationContext _context;
        private IDisposable _subscription;
        private CollisionWorld _world;

        protected override void OnCreate()
        {
            base.OnCreate();
            RequireForUpdate<NetworkStreamInGame>();
        }

        protected override void OnDestroy() => _subscription?.Dispose();

        protected override void OnUpdate()
        {
            if (!TryGetContext(out var context)) return;
            Bind(context);
            var effects = context.Effects;
            var settings = effects.Settings;
            if (settings == null) return;
            var now = UnityEngine.Time.time;

            if (_explosions.Count > 0)
            {
                var hasWorld = SystemAPI.TryGetSingleton<PhysicsWorldSingleton>(out var physics);
                if (hasWorld) _world = physics.CollisionWorld;
                foreach (var blast in _explosions)
                {
                    _sequence.Start(effects, blast.Position, blast.Velocity, now, hasWorld ? CastStatic : null);
                    context.HeatMarks.AddTimed(blast.Position, VisionMath.BlastHeat * 2f, VisionMath.BlastSeconds * 2f);
                    Shake(context, settings, blast.Position);
                }
                _explosions.Clear();
                _world = default;
            }

            _sequence.Tick(effects, now);
        }

        /// <summary>First static hit on the segment: the wreck itself and other choppers are dynamic bodies.</summary>
        private bool CastStatic(Vector3 from, Vector3 to, out Vector3 point, out float fraction)
        {
            point = to;
            fraction = 1f;
            var hits = new NativeList<Unity.Physics.RaycastHit>(8, Allocator.Temp);
            try
            {
                var ray = new RaycastInput { Start = from, End = to, Filter = CollisionFilter.Default };
                if (!_world.CastRay(ray, ref hits)) return false;
                var best = 2f;
                foreach (var hit in hits)
                {
                    if (hit.RigidBodyIndex < _world.NumDynamicBodies || hit.Fraction >= best) continue;
                    best = hit.Fraction;
                    point = hit.Position;
                }
                fraction = best;
                return best <= 1f;
            }
            finally
            {
                hits.Dispose();
            }
        }

        private static void Shake(PresentationContext context, CombatEffectsSettings settings, Vector3 position)
        {
            var rig = context.CameraRig;
            if (rig == null) return;
            var distance = Vector3.Distance(rig.transform.position, position);
            var share = 1f - Mathf.Clamp01(distance / settings.ChopperExplosionShakeRange);
            if (share > 0f) rig.AddShake(settings.ChopperExplosionShake * share * share);
        }

        /// <summary>New context (scene change): pending blasts belonged to the old one.</summary>
        private void Bind(PresentationContext context)
        {
            if (_context == context) return;
            _subscription?.Dispose();
            _explosions.Clear();
            _sequence.Clear();
            _context = context;
            _subscription = context.Events.Subscribe<WreckExploded>(m => _explosions.Add(m));
        }
    }
}
