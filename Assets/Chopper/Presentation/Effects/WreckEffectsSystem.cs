using System;
using System.Collections.Generic;
using Chopper.Presentation.Bridge;
using Chopper.Simulation.Combat;
using Chopper.Presentation.Events;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Client only. A wreck's explosion where the server says it blew up (<see cref="WreckExploded"/>,
    /// Docs/ChopperDeathDesign.md): the chopper explosion, or (none set) three layered missile blasts, the main one at
    /// the point and two smaller ones a little along its travel; a heat mark; and camera shake that falls off with
    /// distance. The falling smoke and fire come from <see cref="DamageEffectsSystem"/>. The break-up: <see cref="WreckDebris"/>.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class WreckEffectsSystem : PresentationSystemBase
    {
        /// <summary>Delays (s) and offsets (along travel, m) of the layered fallback blasts.</summary>
        private static readonly (float Delay, float Ahead, float Up)[] Layers = { (0f, 0f, 0f), (0.12f, 3f, 1.5f), (0.26f, -2f, 3f) };

        private readonly List<WreckExploded> _explosions = new();
        private readonly List<(float At, Vector3 Position)> _pending = new();
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
            var now = UnityEngine.Time.time;

            foreach (var blast in _explosions)
            {
                var travel = blast.Velocity.sqrMagnitude > 1f ? blast.Velocity.normalized : Vector3.forward;
                if (settings.ChopperExplosion != settings.MissileImpact)
                {
                    effects.SpawnOneShot(settings.ChopperExplosion, blast.Position, Quaternion.identity);
                }
                else
                {
                    foreach (var (delay, ahead, up) in Layers)
                        _pending.Add((now + delay, blast.Position + travel * ahead + Vector3.up * up));
                }
                context.HeatMarks.AddTimed(blast.Position, VisionMath.BlastHeat * 2f, VisionMath.BlastSeconds * 2f);
                Shake(context, settings, blast.Position);
            }
            _explosions.Clear();

            for (var i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].At > now) continue;
                effects.SpawnOneShot(settings.MissileImpact, _pending[i].Position, Quaternion.identity);
                _pending.RemoveAt(i);
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
            _pending.Clear();
            _context = context;
            _subscription = context.Events.Subscribe<WreckExploded>(m => _explosions.Add(m));
        }
    }
}
