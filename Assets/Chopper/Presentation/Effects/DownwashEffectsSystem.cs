using System.Collections.Generic;
using Chopper.Presentation.Bridge;
using Chopper.Simulation.Flight;
using Chopper.Simulation.Visuals;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Client only. Rotor downwash (plan 7c2): when a chopper flies low, a ring of dust blows out from the surface below
    /// it. A ray down from each chopper finds the surface; strength grows as the chopper gets lower and with collective.
    /// The ring gets smaller and faster as it gets lower. Own chopper: a little camera shake. Cosmetic only, every chopper
    /// (remote ones too). One particle system per chopper, made in code (settings and material in
    /// <see cref="CombatEffectsSettings"/>).
    /// Dust colour: the map's height fog colour when a city is loaded (its sand or ash), else the settings' colour.
    /// Lit particles: at night the chopper's spotlight shows the dust. Checked in the editor 5 Nov 2026.
    /// Next: surface type from what the ray hits (water spray, snow, grass), and weather (wet ground = less dust).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class DownwashEffectsSystem : PresentationSystemBase
    {
        /// <summary>Ground steeper than this (normal y) gets no dust: walls, cliffs.</summary>
        private const float MinSurfaceUp = 0.5f;
        /// <summary>How fast the strength follows the target (1/s), so the dust does not flicker.</summary>
        private const float Response = 6f;
        private const int MaxParticles = 600;

        private sealed class Emitter
        {
            public ParticleSystem System;
            public float Strength;
        }

        private readonly Dictionary<Entity, Emitter> _emitters = new();
        private readonly HashSet<Entity> _seen = new();
        private readonly List<Entity> _gone = new();
        private PresentationContext _context;
        private Transform _root;

        protected override void OnCreate()
        {
            base.OnCreate();
            RequireForUpdate<NetworkStreamInGame>();
        }

        protected override void OnDestroy() => Clear();

        protected override void OnUpdate()
        {
            if (!TryGetContext(out var context) || context.Effects.Settings == null)
            {
                Clear();
                return;
            }
            if (_context != context)
            {
                Clear();
                _context = context;
            }
            var settings = context.Effects.Settings;
            if (settings.DownwashMaterial == null || settings.DownwashHeight <= 0f) return;
            // The map's dust: city maps carry a height fog whose colour is their air-borne dust (sand, ash).
            var dust = settings.DownwashColour;
            if (SystemAPI.TryGetSingleton<CityAtmosphere>(out var atmosphere))
            {
                var fog = new Color(atmosphere.Colour.x, atmosphere.Colour.y, atmosphere.Colour.z).gamma;
                dust = new Color(fog.r, fog.g, fog.b, dust.a);
            }
            if (!SystemAPI.TryGetSingleton<PhysicsWorldSingleton>(out var physics)) return;
            var world = physics.CollisionWorld;
            var deltaTime = SystemAPI.Time.DeltaTime;

            _seen.Clear();
            foreach (var (pose, flight, entity) in SystemAPI
                         .Query<RefRO<LocalToWorld>, RefRO<ChopperFlightState>>()
                         .WithAll<ChopperFlightConfig>()
                         .WithEntityAccess())
            {
                _seen.Add(entity);
                float3 position = pose.ValueRO.Position;
                var target = 0f;
                var height = settings.DownwashHeight;
                var hitPoint = position;
                var normal = new float3(0f, 1f, 0f);
                if (CastDown(world, entity, position, settings.DownwashHeight, out var hit) && hit.SurfaceNormal.y >= MinSurfaceUp)
                {
                    height = math.distance(position, hit.Position);
                    hitPoint = hit.Position;
                    normal = hit.SurfaceNormal;
                    var near = math.saturate(1f - height / settings.DownwashHeight);
                    target = near * near * (0.7f + 0.3f * math.saturate(flight.ValueRO.Collective));
                }

                if (!_emitters.TryGetValue(entity, out var emitter))
                {
                    if (target <= 0f) continue;
                    emitter = new Emitter { System = Create(settings, dust) };
                    _emitters[entity] = emitter;
                }
                emitter.Strength = math.lerp(emitter.Strength, target, 1f - math.exp(-Response * deltaTime));
                Drive(emitter, settings, hitPoint, normal, height);

                var isLocal = SystemAPI.HasComponent<GhostOwnerIsLocal>(entity) && SystemAPI.IsComponentEnabled<GhostOwnerIsLocal>(entity);
                if (isLocal && context.CameraRig != null && emitter.Strength > 0.05f)
                    context.CameraRig.AddShake(settings.DownwashShake * emitter.Strength * deltaTime);
            }
            RemoveMissing();
        }

        /// <summary>Nearest surface straight down within <paramref name="range"/>, not the chopper itself.</summary>
        private static bool CastDown(in CollisionWorld world, Entity self, float3 start, float range, out Unity.Physics.RaycastHit closest)
        {
            var ray = new RaycastInput { Start = start, End = start - new float3(0f, range, 0f), Filter = CollisionFilter.Default };
            var hits = new NativeList<Unity.Physics.RaycastHit>(8, Allocator.Temp);
            closest = default;
            var best = 2f;
            if (world.CastRay(ray, ref hits))
            {
                foreach (var hit in hits)
                {
                    if (hit.Entity == self || hit.Fraction >= best) continue;
                    best = hit.Fraction;
                    closest = hit;
                }
            }
            hits.Dispose();
            return best <= 1f;
        }

        /// <summary>Places the ring on the surface and sets how much dust it throws and how fast.</summary>
        private static void Drive(Emitter emitter, CombatEffectsSettings settings, float3 point, float3 normal, float height)
        {
            var system = emitter.System;
            if (system == null) return;
            var transform = system.transform;
            // The circle shape lies in the local XY plane: its forward axis on the surface normal.
            transform.SetPositionAndRotation((Vector3)(point + normal * 0.3f), Quaternion.LookRotation((Vector3)normal));

            var near = math.saturate(1f - height / settings.DownwashHeight);
            var shape = system.shape;
            shape.radius = math.lerp(settings.DownwashRadius.x, settings.DownwashRadius.y, near);
            var main = system.main;
            main.startSpeedMultiplier = settings.DownwashSpeed * (0.4f + 0.6f * emitter.Strength);
            var emission = system.emission;
            emission.rateOverTimeMultiplier = settings.DownwashRate * emitter.Strength;

            if (emitter.Strength > 0.01f && !system.isEmitting) system.Play();
            else if (emitter.Strength <= 0.01f && system.isEmitting) system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        /// <summary>A world-space ring emitter: puffs move out along the surface, slow down, rise a little, grow and fade.</summary>
        private ParticleSystem Create(CombatEffectsSettings settings, Color colour)
        {
            if (_root == null) _root = new GameObject("Rotor Downwash").transform;
            var go = new GameObject("Downwash");
            go.transform.SetParent(_root, false);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = MaxParticles;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.7f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(settings.DownwashSize * 0.6f, settings.DownwashSize * 1.4f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(colour, colour * new Color(0.8f, 0.8f, 0.8f, 1f));
            main.gravityModifier = -0.04f;

            var emission = system.emission;
            emission.rateOverTime = 0f;

            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = settings.DownwashRadius.x;
            shape.radiusThickness = 0.35f;

            // Lift off the ground as they slow: a little velocity along the surface normal (local Z).
            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            // All three axes in the same curve mode (two constants), or Unity rejects them.
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.z = new ParticleSystem.MinMaxCurve(0.5f, 2.5f);

            var limit = system.limitVelocityOverLifetime;
            limit.enabled = true;
            // Fast out, then the dust hangs: speed above 2 m/s bleeds away.
            limit.limit = new ParticleSystem.MinMaxCurve(2f);
            limit.dampen = 0.04f;

            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 1.8f));

            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
            var colourOverLifetime = system.colorOverLifetime;
            colourOverLifetime.enabled = true;
            colourOverLifetime.color = new ParticleSystem.MinMaxGradient(fade);

            var rotation = system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = settings.DownwashMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.maxParticleSize = 2f;
            return system;
        }

        private void RemoveMissing()
        {
            _gone.Clear();
            foreach (var entity in _emitters.Keys)
                if (!_seen.Contains(entity)) _gone.Add(entity);
            foreach (var entity in _gone)
            {
                var system = _emitters[entity].System;
                if (system != null)
                {
                    // Let the last dust fade, then remove it.
                    system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    Object.Destroy(system.gameObject, 3f);
                }
                _emitters.Remove(entity);
            }
        }

        private void Clear()
        {
            foreach (var emitter in _emitters.Values)
                if (emitter.System != null) Object.Destroy(emitter.System.gameObject);
            _emitters.Clear();
            if (_root != null) Object.Destroy(_root.gameObject);
            _root = null;
            _context = null;
        }
    }
}
