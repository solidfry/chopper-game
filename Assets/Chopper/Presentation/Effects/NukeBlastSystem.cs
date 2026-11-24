using System;
using System.Collections.Generic;
using Chopper.Presentation.Bridge;
using Chopper.Presentation.Events;
using Chopper.Simulation.Combat;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Physics;
using UnityEngine;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Client only. Capture the Nuke: the nuke going off in a base (<see cref="NukeDetonated"/>), with the parts of a
    /// tactical nuke (owner, 24 Nov). The blast prefab (<see cref="CombatEffectsSettings.NukeBlast"/>) on the ground
    /// under the burst, animated by <see cref="NukeBlastTimeline"/>: the fireball swells and rises into the mushroom,
    /// its lava cracks cool, the stem draws up, shock rings run out round the cap, wind strokes and dust puffs run out on
    /// the ground, then it all dissolves. On top: a screen flash (stronger when you look at it), a light that fades
    /// white to orange, the EMP (HUD glitch near the blast, at the flash), and the shake when the shock front reaches
    /// the camera (at <see cref="CombatEffectsSettings.NukeShockSpeed"/>), plus pack ground fires. Looks only.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class NukeBlastSystem : PresentationSystemBase
    {
        private const float GroundSearch = 400f;
        private const float EmpSeconds = 1.8f;

        private readonly List<NukeDetonated> _incoming = new();
        private readonly List<(float At, Vector3 Position, int Layer)> _pending = new();
        private readonly NukeBlastPlayer _player = new();
        private PresentationContext _context;
        private IDisposable _subscription;
        private Action<Vector3> _shake;

        protected override void OnCreate()
        {
            base.OnCreate();
            RequireForUpdate<NetworkStreamInGame>();
        }

        protected override void OnDestroy()
        {
            _subscription?.Dispose();
            _player.Clear();
        }

        protected override void OnUpdate()
        {
            if (!TryGetContext(out var context)) return;
            Bind(context);
            var effects = context.Effects;
            var settings = effects.Settings;
            if (settings == null) return;
            var now = UnityEngine.Time.time;

            foreach (var detonation in _incoming) Begin(context, settings, detonation, now);
            _incoming.Clear();

            for (var i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].At > now) continue;
                var (_, position, layer) = _pending[i];
                _pending.RemoveAt(i);
                if (layer < settings.NukeBlastLayers.Count) effects.SpawnLayer(settings.NukeBlastLayers[layer], position);
            }

            _player.Tick(settings, now, _shake);
        }

        private void Begin(PresentationContext context, CombatEffectsSettings settings, NukeDetonated detonation, float now)
        {
            var ground = FindGround(detonation.Position);
            var layers = settings.NukeBlastLayers;
            for (var i = 0; i < layers.Count; i++)
                _pending.Add((now + layers[i].Delay, layers[i].Ground ? ground : detonation.Position, i));
            context.HeatMarks.AddTimed(detonation.Position, VisionMath.BlastHeat * 6f, VisionMath.BlastSeconds * 6f);

            var camera = UnityEngine.Camera.main;
            var distance = camera != null ? Vector3.Distance(camera.transform.position, detonation.Position) : float.MaxValue;
            // EMP: at the speed of light, so at the flash.
            if (settings.NukeEmpRadius > 0f && distance < settings.NukeEmpRadius)
                context.HudLook?.Disrupt(Mathf.Lerp(1f, 0.4f, distance / settings.NukeEmpRadius), EmpSeconds);
            _player.Start(settings, detonation.Position, ground, camera, now);
        }

        /// <summary>The first static surface below the blast; the blast point when there is none.</summary>
        private Vector3 FindGround(Vector3 position)
        {
            if (!SystemAPI.TryGetSingleton<PhysicsWorldSingleton>(out var physics)) return position;
            var world = physics.CollisionWorld;
            var hits = new NativeList<Unity.Physics.RaycastHit>(8, Allocator.Temp);
            try
            {
                var ray = new RaycastInput { Start = position, End = position + Vector3.down * GroundSearch, Filter = CollisionFilter.Default };
                if (!world.CastRay(ray, ref hits)) return position;
                var best = 2f;
                var point = position;
                foreach (var hit in hits)
                {
                    if (hit.RigidBodyIndex < world.NumDynamicBodies || hit.Fraction >= best) continue;
                    best = hit.Fraction;
                    point = hit.Position;
                }
                return point;
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
            var share = 1f - Mathf.Clamp01(distance / settings.NukeBlastShakeRange);
            if (share > 0f) rig.AddShake(settings.NukeBlastShake * share);
        }

        /// <summary>New context (scene change): pending layers and blasts belonged to the old scene.</summary>
        private void Bind(PresentationContext context)
        {
            if (_context == context) return;
            _subscription?.Dispose();
            _incoming.Clear();
            _pending.Clear();
            _player.Clear();
            _context = context;
            _shake = centre =>
            {
                var settings = context.Effects.Settings;
                if (settings != null) Shake(context, settings, centre);
            };
            _subscription = context.Events.Subscribe<NukeDetonated>(_incoming.Add);
        }
    }
}
