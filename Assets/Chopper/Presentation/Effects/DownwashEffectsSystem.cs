using System.Collections.Generic;
using Chopper.Presentation.Bridge;
using Chopper.Simulation.Flight;
using Chopper.Simulation.Visuals;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
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
    /// 18 Nov 2026: the rays are one Burst job for all choppers (<see cref="CastJob"/>); managed ray casts were ~0.5 ms.
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
            // All rays at once in Burst, then the managed particle work per chopper.
            var choppers = new NativeList<Entity>(16, Allocator.TempJob);
            var starts = new NativeList<float3>(16, Allocator.TempJob);
            foreach (var (pose, entity) in SystemAPI.Query<RefRO<LocalToWorld>>()
                         .WithAll<ChopperFlightConfig, ChopperFlightState>().WithEntityAccess())
            {
                choppers.Add(entity);
                starts.Add(pose.ValueRO.Position);
            }
            var hits = new NativeArray<Unity.Physics.RaycastHit>(choppers.Length, Allocator.TempJob);
            var found = new NativeArray<bool>(choppers.Length, Allocator.TempJob);
            new CastJob
            {
                World = world, Choppers = choppers.AsArray(), Starts = starts.AsArray(), Range = settings.DownwashHeight,
                Hits = hits, Found = found,
            }.Run(choppers.Length);

            for (var c = 0; c < choppers.Length; c++)
            {
                var entity = choppers[c];
                var flight = SystemAPI.GetComponentRO<ChopperFlightState>(entity);
                _seen.Add(entity);
                var position = starts[c];
                var target = 0f;
                var height = settings.DownwashHeight;
                var hitPoint = position;
                var normal = new float3(0f, 1f, 0f);
                var hit = hits[c];
                if (found[c] && hit.SurfaceNormal.y >= MinSurfaceUp)
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
            hits.Dispose();
            found.Dispose();
            choppers.Dispose();
            starts.Dispose();
            RemoveMissing();
        }

        /// <summary>Per chopper: the nearest surface straight down within <see cref="Range"/>, not the chopper itself.</summary>
        [BurstCompile]
        private struct CastJob : IJobFor
        {
            [ReadOnly] public CollisionWorld World;
            [ReadOnly] public NativeArray<Entity> Choppers;
            [ReadOnly] public NativeArray<float3> Starts;
            public float Range;
            public NativeArray<Unity.Physics.RaycastHit> Hits;
            public NativeArray<bool> Found;

            public void Execute(int index)
            {
                Found[index] = CastDown(World, Choppers[index], Starts[index], Range, out var hit);
                Hits[index] = hit;
            }
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
            transform.SetPositionAndRotation((Vector3)(point + normal * 1.1f), Quaternion.LookRotation((Vector3)normal));

            var near = math.saturate(1f - height / settings.DownwashHeight);
            var shape = system.shape;
            shape.radius = math.lerp(settings.DownwashRadius.x, settings.DownwashRadius.y, near);
            var main = system.main;
            var speed = settings.DownwashSpeed * (0.4f + 0.6f * emitter.Strength);
            main.startSpeedMultiplier = speed;
            var limit = system.limitVelocityOverLifetime;
            limit.limitMultiplier = speed;
            var emission = system.emission;
            emission.rateOverTimeMultiplier = settings.DownwashRate * emitter.Strength;

            if (emitter.Strength > 0.01f && !system.isEmitting) system.Play();
            else if (emitter.Strength <= 0.01f && system.isEmitting) system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        /// <summary>A world-space ring emitter: puffs move out along the surface, slow down, rise a little, grow and fade.</summary>
        /// <summary>
        /// Real rotor wash: a sheet of dust thrown out fast and low along the ground from under the rotor, slowing as it
        /// spreads, then churning and rolling up at the edge. Smoke flipbook texture (<see cref="SmokeSheet"/>), noise for
        /// turbulence, puffs that grow and thin as they slow. (Before 18 Nov 2026: round blobs held to 2 m/s, which hung
        /// in a ring and looked fake.)
        /// </summary>
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
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);   // x DownwashSpeed (Drive)
            // Big puffs: a real wash cloud is metres high; small puffs vanish in the soft fade at the ground.
            main.startSize = new ParticleSystem.MinMaxCurve(settings.DownwashSize * 1.15f, settings.DownwashSize * 2.15f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(colour, colour * new Color(0.82f, 0.8f, 0.78f, 1f));
            main.gravityModifier = 0f;

            var emission = system.emission;
            emission.rateOverTime = 0f;

            // The circle lies on the ground (local XY) and throws particles out along its radius.
            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = settings.DownwashRadius.x;
            shape.radiusThickness = 0.6f;
            shape.randomDirectionAmount = 0.08f;

            // Low at first, then the edge of the sheet rolls up off the ground (local Z = surface normal).
            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            var flat = AnimationCurve.Constant(0f, 1f, 0f);
            var rise = new AnimationCurve(new Keyframe(0f, 0.05f), new Keyframe(0.35f, 0.25f), new Keyframe(1f, 1f));
            velocity.x = new ParticleSystem.MinMaxCurve(1f, flat);
            velocity.y = new ParticleSystem.MinMaxCurve(1f, flat);
            velocity.z = new ParticleSystem.MinMaxCurve(3.2f, rise);

            // Drag: fast out, then it slows to a drift (the limit falls over the life; Drive scales it by speed).
            var limit = system.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(0.3f, 0.45f), new Keyframe(0.7f, 0.14f), new Keyframe(1f, 0.08f)));
            limit.dampen = 0.12f;

            // Turbulence: the sheet churns and breaks up.
            var noise = system.noise;
            noise.enabled = true;
            // Mostly sideways: up/down churn pushed puffs into the ground.
            noise.separateAxes = true;
            noise.strengthX = new ParticleSystem.MinMaxCurve(1.6f);
            noise.strengthY = new ParticleSystem.MinMaxCurve(0.4f);
            noise.strengthZ = new ParticleSystem.MinMaxCurve(1.6f);
            noise.frequency = 0.22f;
            noise.scrollSpeed = 0.5f;
            noise.damping = true;
            noise.octaveCount = 2;
            noise.quality = ParticleSystemNoiseQuality.Medium;

            // Puffs grow as they slow and spread.
            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.35f), new Keyframe(0.4f, 1.3f), new Keyframe(1f, 2.6f)));

            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.55f, 0.55f), new GradientAlphaKey(0f, 1f) });
            var colourOverLifetime = system.colorOverLifetime;
            colourOverLifetime.enabled = true;
            colourOverLifetime.color = new ParticleSystem.MinMaxGradient(fade);

            var rotation = system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);

            SmokeSheet.Apply(system);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = settings.DownwashMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.maxParticleSize = 3f;
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
