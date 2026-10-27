using System;
using System.Collections.Generic;
using Chopper.Presentation.Bridge;
using Chopper.Presentation.Events;
using Chopper.Simulation.Combat;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Client only. A pooled visual follows every guided missile ghost (the shooter's predicted copy and everyone
    /// else's interpolated one), a launch flash when it appears, its trail fades when the ghost goes, and an impact
    /// where the server says it exploded (<see cref="MissileDetonated"/>).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class MissileEffectsSystem : PresentationSystemBase
    {
        private readonly Dictionary<Entity, GameObject> _visuals = new();
        private readonly HashSet<Entity> _seen = new();
        private readonly List<Entity> _gone = new();
        private readonly List<Vector3> _detonations = new();
        private PresentationContext _context;
        private IDisposable _subscription;

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

            _seen.Clear();
            foreach (var (pose, entity) in SystemAPI.Query<RefRO<LocalToWorld>>().WithAll<GuidedMissile>().WithEntityAccess())
            {
                _seen.Add(entity);
                var position = pose.ValueRO.Position;
                var rotation = pose.ValueRO.Rotation;
                if (!_visuals.TryGetValue(entity, out var visual))
                {
                    effects.SpawnOneShot(settings.RocketMuzzleFlash, position, rotation);
                    visual = effects.SpawnPooled(settings.Missile, position, rotation);
                    _visuals[entity] = visual;
                }
                if (visual != null) visual.transform.SetPositionAndRotation(position, rotation);
            }

            // Ghost removed (exploded or expired on the server): let the trail fade.
            _gone.Clear();
            foreach (var entity in _visuals.Keys)
                if (!_seen.Contains(entity)) _gone.Add(entity);
            foreach (var entity in _gone)
            {
                effects.Release(_visuals[entity], settings.RocketTrailLinger);
                _visuals.Remove(entity);
            }

            foreach (var position in _detonations)
                effects.SpawnOneShot(settings.MissileImpact, position, Quaternion.identity);
            _detonations.Clear();
        }

        /// <summary>New context (scene change): old visuals went with the old pool.</summary>
        private void Bind(PresentationContext context)
        {
            if (_context == context) return;
            _subscription?.Dispose();
            _visuals.Clear();
            _detonations.Clear();
            _context = context;
            _subscription = context.Events.Subscribe<MissileDetonated>(m => _detonations.Add(m.Position));
        }
    }
}
