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
    /// Client only. Capture the Nuke: the nuke going off in a base (<see cref="NukeDetonated"/>). The layered blast from
    /// <see cref="CombatEffectsSettings.NukeBlastLayers"/> over time (ground layers on the ground under the blast: the
    /// nuke goes off under its carrier), a light flash that fades, a big heat mark and camera shake out to
    /// <see cref="CombatEffectsSettings.NukeBlastShakeRange"/>. Looks only: the blast hurts nobody (the round ends).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class NukeBlastSystem : PresentationSystemBase
    {
        /// <summary>How far down a ground layer looks for the ground (m).</summary>
        private const float GroundSearch = 400f;

        private readonly List<NukeDetonated> _blasts = new();
        private readonly List<(float At, Vector3 Position, int Layer)> _pending = new();
        private readonly List<(Light Light, float Start)> _flashes = new();
        private PresentationContext _context;
        private IDisposable _subscription;

        protected override void OnCreate()
        {
            base.OnCreate();
            RequireForUpdate<NetworkStreamInGame>();
        }

        protected override void OnDestroy()
        {
            _subscription?.Dispose();
            ClearFlashes();
        }

        protected override void OnUpdate()
        {
            if (!TryGetContext(out var context)) return;
            Bind(context);
            var effects = context.Effects;
            var settings = effects.Settings;
            if (settings == null) return;
            var now = UnityEngine.Time.time;

            foreach (var blast in _blasts)
            {
                var ground = FindGround(blast.Position);
                var layers = settings.NukeBlastLayers;
                for (var i = 0; i < layers.Count; i++)
                    _pending.Add((now + layers[i].Delay, layers[i].Ground ? ground : blast.Position, i));
                Flash(settings, blast.Position, now);
                context.HeatMarks.AddTimed(blast.Position, VisionMath.BlastHeat * 6f, VisionMath.BlastSeconds * 6f);
                Shake(context, settings, blast.Position);
            }
            _blasts.Clear();

            for (var i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].At > now) continue;
                var (_, position, layer) = _pending[i];
                _pending.RemoveAt(i);
                if (layer < settings.NukeBlastLayers.Count) effects.SpawnLayer(settings.NukeBlastLayers[layer], position);
            }

            for (var i = _flashes.Count - 1; i >= 0; i--)
            {
                var (light, start) = _flashes[i];
                var t = (now - start) / settings.NukeFlashSeconds;
                if (light == null || t >= 1f)
                {
                    if (light != null) UnityEngine.Object.Destroy(light.gameObject);
                    _flashes.RemoveAt(i);
                    continue;
                }
                // A hard white-hot peak, then a long orange fade.
                light.intensity = settings.NukeFlashIntensity * (1f - t) * (1f - t);
                light.color = Color.Lerp(settings.NukeFlashColour, new Color(1f, 0.45f, 0.15f), t);
            }
        }

        private void Flash(CombatEffectsSettings settings, Vector3 position, float now)
        {
            if (settings.NukeFlashIntensity <= 0f) return;
            var go = new GameObject("NukeFlash");
            go.transform.position = position + Vector3.up * 20f;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = settings.NukeFlashRange;
            light.color = settings.NukeFlashColour;
            light.intensity = settings.NukeFlashIntensity;
            light.shadows = LightShadows.None;
            _flashes.Add((light, now));
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

        private void ClearFlashes()
        {
            foreach (var (light, _) in _flashes)
                if (light != null) UnityEngine.Object.Destroy(light.gameObject);
            _flashes.Clear();
        }

        /// <summary>New context (scene change): pending layers and flashes belonged to the old scene.</summary>
        private void Bind(PresentationContext context)
        {
            if (_context == context) return;
            _subscription?.Dispose();
            _blasts.Clear();
            _pending.Clear();
            ClearFlashes();
            _context = context;
            _subscription = context.Events.Subscribe<NukeDetonated>(_blasts.Add);
        }
    }
}
