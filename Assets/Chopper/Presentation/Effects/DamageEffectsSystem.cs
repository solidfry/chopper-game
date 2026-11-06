using System;
using System.Collections.Generic;
using Chopper.Presentation.Bridge;
using Chopper.Presentation.Events;
using Chopper.Simulation.Combat;
using Chopper.Simulation.Visuals;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Client only. The damage model's look (plan backlog "Damage model"), for every chopper and training target:
    /// <list type="bullet">
    /// <item>Sparks fly off the hull where the server says it was hit (<see cref="ChopperHit"/>), along the surface
    /// normal; crashes and hard landings also throw up dust in the map's colour.</item>
    /// <item>The last <see cref="DamageLook.MaxPoints"/> hit points stay on the hull (local space). As health runs
    /// low they smoke (light grey, then black) and at the end burn. Health is the replicated ghost field, so late
    /// joiners see the smoke too (from the hull centre until a new hit marks a point).</item>
    /// </list>
    /// Cosmetic only. Four shared world-space particle systems made in code (sparks, flames, smoke, dust); materials
    /// and numbers in <see cref="CombatEffectsSettings"/> ("Damage"). Own chopper in a cockpit view: nothing is drawn
    /// within <see cref="CockpitClearance"/> of the eye, so the smoke does not fill the canopy.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class DamageEffectsSystem : PresentationSystemBase
    {
        private const float CockpitClearance = 2.5f;
        /// <summary>Share of the chopper's own velocity a smoke puff keeps when it leaves the hull.</summary>
        private const float SmokeInherit = 0.35f;
        private const float FlameRate = 22f;

        private sealed class Damaged
        {
            public readonly List<(Vector3 Point, Vector3 Normal)> Points = new();
            public Vector3 LastPosition;
            public bool HasPosition;
            public float SmokeCarry;
            public float FlameCarry;
        }

        private readonly Dictionary<Entity, Damaged> _damaged = new();
        private readonly Dictionary<int, Entity> _byGhostId = new();
        private readonly List<ChopperHit> _pending = new();
        private readonly HashSet<Entity> _seen = new();
        private readonly List<Entity> _gone = new();
        private readonly List<(Vector3 Point, Vector3 Normal)> _centre = new() { (new Vector3(0f, 0.5f, 0f), Vector3.up) };
        private PresentationContext _context;
        private IDisposable _subscription;
        private Transform _root;
        private ParticleSystem _sparks;
        private ParticleSystem _flames;
        private ParticleSystem _smoke;
        private ParticleSystem _dust;

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
            Bind(context);
            var settings = context.Effects.Settings;
            var deltaTime = SystemAPI.Time.DeltaTime;
            var rig = context.CameraRig;
            var cockpit = rig != null && rig.HidesOwnChopper;
            var eye = rig != null ? rig.transform.position : Vector3.zero;

            ApplyHits(settings);

            _seen.Clear();
            foreach (var (health, pose, entity) in SystemAPI.Query<RefRO<Health>, RefRO<LocalToWorld>>().WithEntityAccess())
            {
                _seen.Add(entity);
                var max = health.ValueRO.Max;
                var share = max > 0f ? health.ValueRO.Current / max : 1f;
                var smoke = DamageLook.Smoke(share, settings.SmokeBelow);
                if (!_damaged.TryGetValue(entity, out var state))
                {
                    if (smoke <= 0f) continue;
                    _damaged[entity] = state = new Damaged();
                }
                // Repaired (a training target came back): forget the holes.
                if (share >= 0.999f) state.Points.Clear();

                Vector3 position = pose.ValueRO.Position;
                var velocity = state.HasPosition && deltaTime > 0f ? (position - state.LastPosition) / deltaTime : Vector3.zero;
                state.LastPosition = position;
                state.HasPosition = true;
                if (smoke <= 0f || health.ValueRO.IsDead) continue;

                var own = cockpit && SystemAPI.HasComponent<GhostOwnerIsLocal>(entity) && SystemAPI.IsComponentEnabled<GhostOwnerIsLocal>(entity);
                var points = state.Points.Count > 0 ? state.Points : _centre;
                var colour = DamageLook.SmokeColour(share, settings.SmokeBelow, settings.HeavySmokeBelow,
                    settings.LightSmokeColour, settings.HeavySmokeColour);
                var fire = DamageLook.Fire(share, settings.FireBelow);
                var matrix = (Matrix4x4)pose.ValueRO.Value;

                state.SmokeCarry += settings.SmokeRate * smoke * deltaTime;
                state.FlameCarry += FlameRate * fire * deltaTime;
                var smokeCount = (int)state.SmokeCarry;
                var flameCount = (int)state.FlameCarry;
                state.SmokeCarry -= smokeCount;
                state.FlameCarry -= flameCount;
                foreach (var (localPoint, localNormal) in points)
                {
                    var point = matrix.MultiplyPoint3x4(localPoint);
                    if (own && (point - eye).sqrMagnitude < CockpitClearance * CockpitClearance) continue;
                    var normal = matrix.MultiplyVector(localNormal).normalized;
                    EmitSmoke(settings, point, normal, velocity, colour, smoke, smokeCount);
                    EmitFlames(point, normal, velocity, fire, flameCount);
                }
            }

            _gone.Clear();
            foreach (var entity in _damaged.Keys)
                if (!_seen.Contains(entity)) _gone.Add(entity);
            foreach (var entity in _gone) _damaged.Remove(entity);
        }

        /// <summary>Sparks (and crash dust) for this frame's hits; keeps the hit points for the smoke.</summary>
        private void ApplyHits(CombatEffectsSettings settings)
        {
            if (_pending.Count == 0) return;
            _byGhostId.Clear();
            foreach (var (ghost, entity) in SystemAPI.Query<RefRO<GhostInstance>>().WithAll<Health>().WithEntityAccess())
                _byGhostId[ghost.ValueRO.ghostId] = entity;

            var rig = _context.CameraRig;
            var cockpit = rig != null && rig.HidesOwnChopper;
            var eye = rig != null ? rig.transform.position : Vector3.zero;
            foreach (var hit in _pending)
            {
                if (!_byGhostId.TryGetValue(hit.GhostId, out var entity) || !SystemAPI.HasComponent<LocalToWorld>(entity)) continue;
                var matrix = (Matrix4x4)SystemAPI.GetComponent<LocalToWorld>(entity).Value;
                var point = matrix.MultiplyPoint3x4(hit.LocalPoint);
                var normal = matrix.MultiplyVector(hit.LocalNormal).normalized;

                if (DamageLook.LeavesMark(hit.Kind))
                {
                    if (!_damaged.TryGetValue(entity, out var state)) _damaged[entity] = state = new Damaged();
                    // A crash normal points away from what was hit; the hole's smoke comes out of the hull, toward it.
                    var outOfHull = hit.Kind == DamageKind.Impact ? -hit.LocalNormal : hit.LocalNormal;
                    DamageLook.AddPoint(state.Points, hit.LocalPoint, outOfHull);
                }

                // Dust stays on the ground outside the canopy, so the pilot sees it even when the contact is right below.
                if (hit.Kind is DamageKind.Impact or DamageKind.Landing) EmitDust(settings, point, settings.ImpactDust);
                var own = cockpit && SystemAPI.HasComponent<GhostOwnerIsLocal>(entity) && SystemAPI.IsComponentEnabled<GhostOwnerIsLocal>(entity);
                if (own && (point - eye).sqrMagnitude < CockpitClearance * CockpitClearance) continue;
                EmitSparks(settings, point, normal, DamageLook.Sparks(hit.Amount, hit.Kind, settings.SparksPerDamage, settings.SparkCount));
            }
            _pending.Clear();
        }

        private void EmitSparks(CombatEffectsSettings settings, Vector3 point, Vector3 normal, int count)
        {
            if (_sparks == null) return;
            var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (var i = 0; i < count; i++)
            {
                // A cone round the normal, some skimming along the hull.
                var direction = (normal + Random.insideUnitSphere * 0.9f).normalized;
                emit.position = point + normal * 0.05f;
                emit.velocity = direction * (settings.SparkSpeed * Random.Range(0.35f, 1.1f));
                emit.startLifetime = Random.Range(0.25f, 0.7f);
                emit.startSize = Random.Range(0.05f, 0.12f);
                _sparks.Emit(emit, 1);
            }
        }

        private void EmitSmoke(CombatEffectsSettings settings, Vector3 point, Vector3 normal, Vector3 velocity, Color colour,
            float strength, int count)
        {
            if (_smoke == null || count <= 0) return;
            var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false, startColor = colour };
            for (var i = 0; i < count; i++)
            {
                emit.position = point + Random.insideUnitSphere * 0.25f;
                emit.velocity = velocity * SmokeInherit + normal * Random.Range(0.5f, 1.5f) + Vector3.up * Random.Range(0.5f, 1.5f);
                emit.startLifetime = Random.Range(2f, 3.5f) * (0.6f + 0.4f * strength);
                emit.startSize = settings.SmokeSize * Random.Range(0.7f, 1.3f) * (0.6f + 0.4f * strength);
                emit.rotation = Random.Range(0f, 360f);
                _smoke.Emit(emit, 1);
            }
        }

        private void EmitFlames(Vector3 point, Vector3 normal, Vector3 velocity, float strength, int count)
        {
            if (_flames == null || count <= 0) return;
            var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (var i = 0; i < count; i++)
            {
                emit.position = point + Random.insideUnitSphere * 0.15f;
                emit.velocity = velocity * 0.6f + normal * Random.Range(0.5f, 2f) + Vector3.up * Random.Range(1f, 2.5f);
                emit.startLifetime = Random.Range(0.2f, 0.45f);
                emit.startSize = Random.Range(0.5f, 1.1f) * (0.5f + 0.5f * strength);
                emit.rotation = Random.Range(0f, 360f);
                _flames.Emit(emit, 1);
            }
        }

        private void EmitDust(CombatEffectsSettings settings, Vector3 point, int count)
        {
            if (_dust == null || count <= 0) return;
            // The map's dust: city maps carry a height fog whose colour is their sand or ash (as the downwash).
            var colour = settings.DownwashColour;
            if (SystemAPI.TryGetSingleton<CityAtmosphere>(out var atmosphere))
            {
                var fog = new Color(atmosphere.Colour.x, atmosphere.Colour.y, atmosphere.Colour.z).gamma;
                colour = new Color(fog.r, fog.g, fog.b, colour.a);
            }
            var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false, startColor = colour };
            for (var i = 0; i < count; i++)
            {
                var outward = Random.insideUnitCircle.normalized;
                emit.position = point + new Vector3(outward.x, 0.2f, outward.y) * 0.5f;
                emit.velocity = new Vector3(outward.x, Random.Range(0.2f, 0.6f), outward.y) * Random.Range(4f, 10f);
                emit.startLifetime = Random.Range(1f, 2.2f);
                emit.startSize = Random.Range(1.5f, 3f);
                emit.rotation = Random.Range(0f, 360f);
                _dust.Emit(emit, 1);
            }
        }

        /// <summary>New context (scene change): new subscription and new particle systems.</summary>
        private void Bind(PresentationContext context)
        {
            if (_context == context) return;
            Clear();
            _context = context;
            _subscription = context.Events.Subscribe<ChopperHit>(_pending.Add);
            var settings = context.Effects.Settings;
            _root = new GameObject("Damage Effects").transform;
            if (settings.DamageSparkMaterial != null)
            {
                _sparks = Create("Sparks", settings.DamageSparkMaterial, 800, ParticleSystemRenderMode.Stretch, gravity: 1f,
                    Fade(new Color(1f, 0.95f, 0.75f), new Color(1f, 0.45f, 0.1f), 0.7f), grow: 0.6f, drag: 0.02f);
                _flames = Create("Flames", settings.DamageSparkMaterial, 600, ParticleSystemRenderMode.Billboard, gravity: -0.2f,
                    Fade(new Color(1f, 0.75f, 0.3f), new Color(0.9f, 0.2f, 0.05f), 0.25f), grow: 1.6f, drag: 0.1f);
            }
            if (settings.DamageSmokeMaterial != null)
                _smoke = Create("Smoke", settings.DamageSmokeMaterial, 2000, ParticleSystemRenderMode.Billboard, gravity: -0.03f,
                    Fade(Color.white, Color.white, 0.6f), grow: 3f, drag: 0.08f);
            if (settings.DownwashMaterial != null)
                _dust = Create("Crash Dust", settings.DownwashMaterial, 400, ParticleSystemRenderMode.Billboard, gravity: 0.05f,
                    Fade(Color.white, Color.white, 0.5f), grow: 2f, drag: 0.12f);
        }

        /// <summary>Colour over lifetime: <paramref name="start"/> to <paramref name="end"/>, alpha up fast, held, then out.</summary>
        private static Gradient Fade(Color start, Color end, float hold)
        {
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(start, 0f), new GradientColorKey(end, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.05f), new GradientAlphaKey(0.8f, hold), new GradientAlphaKey(0f, 1f) });
            return gradient;
        }

        /// <summary>A world-space system that only emits by script (Emit), with drag, growth and a colour fade.</summary>
        private ParticleSystem Create(string name, Material material, int maxParticles, ParticleSystemRenderMode mode, float gravity,
            Gradient colour, float grow, float drag)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = maxParticles;
            main.gravityModifier = gravity;
            main.startColor = Color.white;

            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;

            var limit = system.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = new ParticleSystem.MinMaxCurve(0f);
            limit.drag = new ParticleSystem.MinMaxCurve(drag);
            limit.multiplyDragByParticleSize = false;
            limit.dampen = 0f;

            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, grow));

            var colourOverLifetime = system.colorOverLifetime;
            colourOverLifetime.enabled = true;
            colourOverLifetime.color = new ParticleSystem.MinMaxGradient(colour);

            if (mode == ParticleSystemRenderMode.Billboard)
            {
                var rotation = system.rotationOverLifetime;
                rotation.enabled = true;
                rotation.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);
            }

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = mode;
            if (mode == ParticleSystemRenderMode.Stretch)
            {
                renderer.velocityScale = 0.05f;
                renderer.lengthScale = 1f;
            }
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.maxParticleSize = 2f;
            system.Play();
            return system;
        }

        private void Clear()
        {
            _subscription?.Dispose();
            _subscription = null;
            _damaged.Clear();
            _pending.Clear();
            if (_root != null) Object.Destroy(_root.gameObject);
            _root = null;
            _sparks = _flames = _smoke = _dust = null;
            _context = null;
        }
    }
}
