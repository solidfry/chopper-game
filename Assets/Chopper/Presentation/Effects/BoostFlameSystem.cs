using System.Collections.Generic;
using Chopper.Presentation.Bridge;
using Chopper.Simulation.Flight;
using Chopper.Simulation.Visuals;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Client only. Boost jet flames: while <see cref="ChopperFlightState.Boosting"/> is on, flames shoot back from the
    /// chopper's exhaust points (<see cref="ExhaustPoint"/>, baked from the Engine sockets). Works for the own predicted
    /// chopper and for interpolated ghosts, as Boosting is a ghost field. A burst of fat particles marks the boost start,
    /// the flame fades out fast when the boost ends. Emitters are pooled; one world-space particle system per exhaust
    /// point, moved to the chopper's pose every frame. Settings and material (the additive spark material) are on
    /// <see cref="CombatEffectsSettings"/>. In the cockpit view the own flames are scaled down so they do not fill the view.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class BoostFlameSystem : PresentationSystemBase
    {
        private const int MaxParticles = 300;
        private const float Life = 0.3f;
        private const float Off = 0.01f;

        private sealed class Flame
        {
            public readonly List<ParticleSystem> Systems = new();
            public float Strength;
        }

        private readonly Dictionary<Entity, Flame> _flames = new();
        private readonly HashSet<Entity> _seen = new();
        private readonly List<Entity> _gone = new();
        private readonly Stack<ParticleSystem> _pool = new();
        /// <summary>Released emitters whose trail is still fading: they stay active until the time is up.</summary>
        private readonly List<(ParticleSystem System, float Until)> _fading = new();
        private PresentationContext _context;
        private Transform _root;

        /// <summary>Strength follows the target: fast up (the kick), faster than the up speed is not needed down (quick fade).</summary>
        public static float Follow(float current, float target, float rise, float fall, float deltaTime)
        {
            var rate = target > current ? rise : fall;
            return math.lerp(current, target, 1f - math.exp(-rate * deltaTime));
        }

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
            if (settings.DamageSparkMaterial == null || settings.BoostFlameLength <= 0f) return;
            var deltaTime = SystemAPI.Time.DeltaTime;
            var cockpitView = context.CameraRig != null && context.CameraRig.HidesOwnChopper;

            _seen.Clear();
            foreach (var (pose, flight, exhaust, entity) in SystemAPI
                         .Query<RefRO<LocalToWorld>, RefRO<ChopperFlightState>, DynamicBuffer<ExhaustPoint>>()
                         .WithEntityAccess())
            {
                var boosting = flight.ValueRO.Boosting;
                if (!_flames.TryGetValue(entity, out var flame))
                {
                    if (!boosting || exhaust.Length == 0) continue;
                    flame = new Flame();
                    for (var i = 0; i < exhaust.Length; i++) flame.Systems.Add(Rent(settings));
                    _flames[entity] = flame;
                    // The kick: the strength starts high and a burst of fat particles leaves each nozzle.
                    flame.Strength = 0.7f;
                    var scale = Scale(settings, cockpitView, entity);
                    for (var i = 0; i < flame.Systems.Count; i++)
                    {
                        Place(flame.Systems[i], exhaust[i], pose.ValueRO);
                        StartTrail(flame.Systems[i], settings);
                        var emit = new ParticleSystem.EmitParams
                        {
                            applyShapeToPosition = true,
                            startSize = settings.BoostFlameSize * 2.2f * scale,
                        };
                        flame.Systems[i].Emit(emit, Mathf.RoundToInt(settings.BoostFlameBurst * scale));
                    }
                }
                _seen.Add(entity);

                flame.Strength = Follow(flame.Strength, boosting ? 1f : 0f, settings.BoostFlameRise, settings.BoostFlameFall, deltaTime);
                var flicker = 1f - settings.BoostFlameFlicker * Random.value;
                var strength = flame.Strength * flicker;
                var factor = Scale(settings, cockpitView, entity);
                var count = math.min(flame.Systems.Count, exhaust.Length);
                for (var i = 0; i < count; i++)
                {
                    var system = flame.Systems[i];
                    Place(system, exhaust[i], pose.ValueRO);
                    var main = system.main;
                    main.startSizeMultiplier = settings.BoostFlameSize * factor * (0.6f + 0.4f * flame.Strength);
                    main.startSpeedMultiplier = settings.BoostFlameLength / Life * (0.5f + 0.5f * strength);
                    var emission = system.emission;
                    emission.rateOverTimeMultiplier = settings.BoostFlameRate * strength * factor;
                    if (flame.Strength > Off && !system.isEmitting) system.Play();
                    var trail = TrailOf(system);
                    if (trail != null) trail.widthMultiplier = settings.BoostTrailWidth * factor * (0.5f + 0.5f * flame.Strength);
                }
            }

            // Trails that have faded out: now the emitter can go back to the pool.
            for (var i = _fading.Count - 1; i >= 0; i--)
            {
                if (UnityEngine.Time.time < _fading[i].Until) continue;
                var done = _fading[i].System;
                _fading.RemoveAt(i);
                if (done == null) continue;
                done.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                done.gameObject.SetActive(false);
                _pool.Push(done);
            }

            // Boost over, or the chopper is gone: stop emitting; the trail fades on its own.
            _gone.Clear();
            foreach (var pair in _flames)
                if (!_seen.Contains(pair.Key) || pair.Value.Strength <= Off) _gone.Add(pair.Key);
            foreach (var entity in _gone)
            {
                foreach (var system in _flames[entity].Systems) Release(system, settings);
                _flames.Remove(entity);
            }
        }

        private float Scale(CombatEffectsSettings settings, bool cockpitView, Entity entity)
        {
            var own = cockpitView && SystemAPI.HasComponent<GhostOwnerIsLocal>(entity) && SystemAPI.IsComponentEnabled<GhostOwnerIsLocal>(entity);
            return own ? settings.BoostFlameCockpitScale : 1f;
        }

        private static void Place(ParticleSystem system, ExhaustPoint point, LocalToWorld pose)
        {
            var position = math.transform(pose.Value, point.LocalPosition);
            var direction = math.rotate(pose.Value, point.LocalDirection);
            if (math.lengthsq(direction) < 1e-6f) direction = new float3(0f, 0f, -1f);
            system.transform.SetPositionAndRotation((Vector3)position, Quaternion.LookRotation((Vector3)direction));
        }

        private ParticleSystem Rent(CombatEffectsSettings settings)
        {
            while (_pool.Count > 0)
            {
                var pooled = _pool.Pop();
                if (pooled == null) continue;
                pooled.gameObject.SetActive(true);
                Configure(pooled, settings);
                var old = TrailOf(pooled);
                if (old != null) old.Clear();
                return pooled;
            }
            if (_root == null) _root = new GameObject("Boost Flames").transform;
            var go = new GameObject("Boost Flame");
            go.transform.SetParent(_root, false);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            Configure(system, settings);
            return system;
        }

        private static TrailRenderer TrailOf(ParticleSystem system) =>
            system != null && system.transform.childCount > 0 ? system.transform.GetChild(0).GetComponent<TrailRenderer>() : null;

        /// <summary>Clear the trail so it draws from the current position, not from where the emitter was before.</summary>
        private static void StartTrail(ParticleSystem system, CombatEffectsSettings settings)
        {
            var trail = TrailOf(system);
            if (trail == null) return;
            trail.Clear();
            trail.emitting = settings.BoostTrailTime > 0f;
        }

        /// <summary>Flame stops; the trail stops growing and fades. The emitter stays active until it has gone.</summary>
        private void Release(ParticleSystem system, CombatEffectsSettings settings)
        {
            if (system == null) return;
            system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            var trail = TrailOf(system);
            if (trail != null) trail.emitting = false;
            _fading.Add((system, UnityEngine.Time.time + Mathf.Max(settings.BoostTrailTime, Life) + 0.1f));
        }

        private void Clear()
        {
            foreach (var flame in _flames.Values)
                foreach (var system in flame.Systems)
                    if (system != null) Object.Destroy(system.gameObject);
            _flames.Clear();
            _fading.Clear();
            _pool.Clear();
            if (_root != null) Object.Destroy(_root.gameObject);
            _root = null;
            _context = null;
        }

        /// <summary>Looks are set on every rent, so a changed settings asset shows up without a restart.</summary>
        private static void Configure(ParticleSystem system, CombatEffectsSettings settings)
        {
            var main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = MaxParticles;
            main.startLifetime = new ParticleSystem.MinMaxCurve(Life * 0.7f, Life * 1.2f);
            main.startSpeed = settings.BoostFlameLength / Life;
            main.startSize = settings.BoostFlameSize;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = settings.BoostFlameCore;

            var emission = system.emission;
            emission.rateOverTime = 0f;

            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 2.5f;
            shape.radius = 0.08f;

            // Core colour at the nozzle, cooling to the tip colour, then dark; the alpha fades at both ends.
            var tip = settings.BoostFlameTip;
            var core = settings.BoostFlameCore;
            var ratio = new Color(
                Mathf.Clamp01(tip.r / Mathf.Max(core.r, 0.01f)),
                Mathf.Clamp01(tip.g / Mathf.Max(core.g, 0.01f)),
                Mathf.Clamp01(tip.b / Mathf.Max(core.b, 0.01f)), 1f);
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(ratio, 0.45f), new GradientColorKey(ratio * 0.5f, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
            var colour = system.colorOverLifetime;
            colour.enabled = true;
            colour.color = new ParticleSystem.MinMaxGradient(gradient);

            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.15f));

            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 2.5f;
            renderer.velocityScale = 0.02f;
            renderer.cameraVelocityScale = 0f;
            renderer.sharedMaterial = settings.DamageSparkMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.maxParticleSize = 1f;

            ConfigureTrail(system, settings);
        }

        /// <summary>A world-space ribbon on a child object (a GameObject cannot have two renderers). Off until a boost starts.</summary>
        private static void ConfigureTrail(ParticleSystem system, CombatEffectsSettings settings)
        {
            var trail = TrailOf(system);
            if (trail == null)
            {
                var child = new GameObject("Trail");
                child.transform.SetParent(system.transform, false);
                trail = child.AddComponent<TrailRenderer>();
            }
            trail.emitting = false;
            trail.time = Mathf.Max(settings.BoostTrailTime, 0.01f);
            trail.minVertexDistance = 0.2f;
            trail.widthMultiplier = settings.BoostTrailWidth;
            trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            var colour = settings.BoostTrailColour;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(colour, 0f), new GradientColorKey(colour * 0.4f, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = gradient;
            trail.alignment = LineAlignment.View;
            trail.numCapVertices = 2;
            trail.sharedMaterial = settings.DamageSparkMaterial;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
        }
    }
}
