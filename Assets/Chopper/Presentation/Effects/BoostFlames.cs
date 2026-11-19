using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// The boost flame look: a stretched-particle flame and a ribbon trail from each exhaust while a chopper boosts, a
    /// burst of fat particles at the kick, a fast fade when it ends. Shared by the game (<see cref="BoostFlameSystem"/>,
    /// keyed by chopper entity) and the effects bench (<see cref="EffectsBench"/>). Call <see cref="Begin"/>, then
    /// <see cref="Update"/> for each chopper, then <see cref="End"/> once per frame.
    /// </summary>
    public sealed class BoostFlames<TKey>
    {
        private const int MaxParticles = 300;
        private const float Life = 0.3f;
        private const float Off = 0.01f;

        private sealed class Flame
        {
            public readonly List<ParticleSystem> Systems = new();
            public float Strength;
        }

        private readonly Dictionary<TKey, Flame> _flames = new();
        private readonly HashSet<TKey> _seen = new();
        private readonly List<TKey> _gone = new();
        private readonly Stack<ParticleSystem> _pool = new();
        /// <summary>Released emitters whose trail is still fading: they stay active until the time is up.</summary>
        private readonly List<(ParticleSystem System, float Until)> _fading = new();
        private Transform _root;

        /// <summary>Strength follows the target: fast up (the kick), faster than the up speed is not needed down (quick fade).</summary>
        public static float Follow(float current, float target, float rise, float fall, float deltaTime)
        {
            var rate = target > current ? rise : fall;
            return math.lerp(current, target, 1f - math.exp(-rate * deltaTime));
        }

        /// <summary>False when the settings turn the flame off (no material or no length).</summary>
        public static bool Enabled(CombatEffectsSettings settings) =>
            settings != null && settings.DamageSparkMaterial != null && settings.BoostFlameLength > 0f;

        public void Begin() => _seen.Clear();

        /// <summary>
        /// One chopper this frame. <paramref name="exhausts"/>: world nozzle points and directions (out of the nozzle).
        /// <paramref name="scale"/>: smaller in your own cockpit view.
        /// </summary>
        public void Update(CombatEffectsSettings settings, TKey key, bool boosting, IReadOnlyList<(Vector3 Position, Vector3 Direction)> exhausts,
            float scale, float deltaTime)
        {
            if (!_flames.TryGetValue(key, out var flame))
            {
                if (!boosting || exhausts.Count == 0) return;
                flame = new Flame();
                for (var i = 0; i < exhausts.Count; i++) flame.Systems.Add(Rent(settings));
                _flames[key] = flame;
                // The kick: the strength starts high and a burst of fat particles leaves each nozzle.
                flame.Strength = 0.7f;
                for (var i = 0; i < flame.Systems.Count; i++)
                {
                    Place(flame.Systems[i], exhausts[i]);
                    StartTrail(flame.Systems[i], settings);
                    var emit = new ParticleSystem.EmitParams { applyShapeToPosition = true, startSize = settings.BoostFlameSize * 2.2f * scale };
                    if (settings.BoostFlameParticles)
                        flame.Systems[i].Emit(emit, Mathf.RoundToInt(settings.BoostFlameBurst * scale));
                }
            }
            _seen.Add(key);

            flame.Strength = Follow(flame.Strength, boosting ? 1f : 0f, settings.BoostFlameRise, settings.BoostFlameFall, deltaTime);
            var flicker = 1f - settings.BoostFlameFlicker * Random.value;
            var strength = flame.Strength * flicker;
            var count = Mathf.Min(flame.Systems.Count, exhausts.Count);
            for (var i = 0; i < count; i++)
            {
                var system = flame.Systems[i];
                Place(system, exhausts[i]);
                var main = system.main;
                main.startSizeMultiplier = settings.BoostFlameSize * scale * (0.6f + 0.4f * flame.Strength);
                main.startSpeedMultiplier = settings.BoostFlameLength / Life * (0.5f + 0.5f * strength);
                var emission = system.emission;
                emission.rateOverTimeMultiplier = settings.BoostFlameParticles ? settings.BoostFlameRate * strength * scale : 0f;
                if (flame.Strength > Off && !system.isEmitting) system.Play();
                var trail = TrailOf(system);
                if (trail != null) trail.widthMultiplier = settings.BoostTrailWidth * scale * (0.5f + 0.5f * flame.Strength);
            }
        }

        /// <summary>Pools faded emitters; stops the flames of choppers not updated this frame or faded out.</summary>
        public void End(CombatEffectsSettings settings)
        {
            for (var i = _fading.Count - 1; i >= 0; i--)
            {
                if (Time.time < _fading[i].Until) continue;
                var done = _fading[i].System;
                _fading.RemoveAt(i);
                if (done == null) continue;
                done.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                done.gameObject.SetActive(false);
                _pool.Push(done);
            }
            _gone.Clear();
            foreach (var pair in _flames)
                if (!_seen.Contains(pair.Key) || pair.Value.Strength <= Off) _gone.Add(pair.Key);
            foreach (var key in _gone)
            {
                foreach (var system in _flames[key].Systems) Release(system, settings);
                _flames.Remove(key);
            }
        }

        private static void Place(ParticleSystem system, (Vector3 Position, Vector3 Direction) exhaust)
        {
            var direction = exhaust.Direction.sqrMagnitude < 1e-6f ? Vector3.back : exhaust.Direction;
            system.transform.SetPositionAndRotation(exhaust.Position, Quaternion.LookRotation(direction));
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
            _fading.Add((system, Time.time + Mathf.Max(settings.BoostTrailTime, Life) + 0.1f));
        }

        public void Clear()
        {
            foreach (var flame in _flames.Values)
                foreach (var system in flame.Systems)
                    if (system != null) Object.Destroy(system.gameObject);
            _flames.Clear();
            _fading.Clear();
            _pool.Clear();
            if (_root != null) Object.Destroy(_root.gameObject);
            _root = null;
        }

        /// <summary>Looks are set on every rent, so a changed settings asset shows up without a restart.</summary>
        private static void Configure(ParticleSystem system, CombatEffectsSettings settings)
        {
            var main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            // Local: the flame is a solid jet cone fixed to the nozzle. World space left loose bits behind (18 Nov).
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
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
            trail.numCornerVertices = 3;   // smooth bends: one continuous ribbon
            trail.sharedMaterial = settings.DamageSparkMaterial;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
        }
    }
}
