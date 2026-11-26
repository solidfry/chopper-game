using System.Collections.Generic;
using UnityEngine;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// A thick white smoke column behind each guided missile (owner, 26 Nov: "more obvious smoke / trails behind them,
    /// almost like an Evangelion style movement"): world-space puffs dropped by distance, so the curving path stays
    /// drawn in the air for a few seconds, and a hot core at the nozzle. Same smoke flipbook as the exhaust smoke, on a
    /// glowing material (<see cref="CombatEffectsSettings.MissileSmokeMaterial"/>) so it reads white at night.
    /// Emitters are pooled; when a missile goes its column stops growing and fades. Call <see cref="Begin"/>, then
    /// <see cref="Update"/> for each missile, then <see cref="End"/> once per frame.
    /// </summary>
    public sealed class MissileSmokeTrails<TKey>
    {
        private const int MaxParticles = 600;

        private readonly Dictionary<TKey, ParticleSystem> _trails = new();
        private readonly HashSet<TKey> _seen = new();
        private readonly List<TKey> _gone = new();
        private readonly Stack<ParticleSystem> _pool = new();
        private readonly List<(ParticleSystem System, float Until)> _fading = new();
        private Transform _root;

        /// <summary>False when the settings turn the trail off (no smoke material or no life).</summary>
        public static bool Enabled(CombatEffectsSettings settings) =>
            settings != null && settings.MissileSmokeMaterial != null && settings.MissileSmokeLife > 0f;

        public void Begin() => _seen.Clear();

        /// <summary>One missile this frame: its tail point (world) and flight direction.</summary>
        public void Update(CombatEffectsSettings settings, TKey key, Vector3 tail, Vector3 forward)
        {
            _seen.Add(key);
            if (!_trails.TryGetValue(key, out var system))
            {
                system = Rent(settings);
                system.transform.SetPositionAndRotation(tail, Quaternion.LookRotation(-forward));
                system.Clear();
                system.Play();
                _trails[key] = system;
                return;
            }
            system.transform.SetPositionAndRotation(tail, Quaternion.LookRotation(-forward));
        }

        /// <summary>Missiles not updated this frame are gone: their columns stop growing and fade.</summary>
        public void End(CombatEffectsSettings settings)
        {
            _gone.Clear();
            foreach (var key in _trails.Keys)
                if (!_seen.Contains(key)) _gone.Add(key);
            foreach (var key in _gone)
            {
                var system = _trails[key];
                _trails.Remove(key);
                if (system == null) continue;
                system.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                _fading.Add((system, Time.time + settings.MissileSmokeLife * 1.2f + 0.2f));
            }
            for (var i = _fading.Count - 1; i >= 0; i--)
            {
                if (_fading[i].System != null && Time.time < _fading[i].Until) continue;
                if (_fading[i].System != null)
                {
                    _fading[i].System.gameObject.SetActive(false);
                    _pool.Push(_fading[i].System);
                }
                _fading.RemoveAt(i);
            }
        }

        /// <summary>Scene change: every emitter goes.</summary>
        public void Clear()
        {
            foreach (var system in _trails.Values)
                if (system != null) Object.Destroy(system.gameObject);
            foreach (var (system, _) in _fading)
                if (system != null) Object.Destroy(system.gameObject);
            while (_pool.Count > 0)
            {
                var system = _pool.Pop();
                if (system != null) Object.Destroy(system.gameObject);
            }
            _trails.Clear();
            _fading.Clear();
            if (_root != null) Object.Destroy(_root.gameObject);
            _root = null;
        }

        private ParticleSystem Rent(CombatEffectsSettings settings)
        {
            while (_pool.Count > 0)
            {
                var pooled = _pool.Pop();
                if (pooled == null) continue;
                pooled.gameObject.SetActive(true);
                Configure(pooled, settings);
                return pooled;
            }
            if (_root == null) _root = new GameObject("MissileSmokeTrails").transform;
            var go = new GameObject("MissileSmoke");
            go.transform.SetParent(_root, false);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            Configure(system, settings);
            return system;
        }

        /// <summary>Set on every rent, so a settings change in play shows on the next missile.</summary>
        private static void Configure(ParticleSystem system, CombatEffectsSettings settings)
        {
            var life = settings.MissileSmokeLife;
            var size = settings.MissileSmokeSize;

            var main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = MaxParticles;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.8f, life * 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.8f, size * 1.2f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var colour = settings.MissileSmokeColour;
            main.startColor = new ParticleSystem.MinMaxGradient(colour, colour * new Color(0.9f, 0.9f, 0.92f, 0.85f));
            main.gravityModifier = -0.01f;

            var emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            // Puffs by distance: an unbroken column along the curve, however fast the missile flies.
            emission.rateOverDistance = 1f / Mathf.Max(0.2f, settings.MissileSmokeSpacing);

            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 6f;
            shape.radius = 0.1f;

            var noise = system.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(0.6f);
            noise.frequency = 0.35f;
            noise.scrollSpeed = 0.25f;
            noise.damping = true;
            noise.octaveCount = 2;
            noise.quality = ParticleSystemNoiseQuality.Medium;

            // Tight at the nozzle, a fat soft column behind (the Itano look: thick white ribbons that hang).
            var grow = system.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(0.15f, 1f),
                new Keyframe(1f, settings.MissileSmokeGrowth)));

            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.82f, 0.6f), 0f), new GradientColorKey(Color.white, 0.06f),
                        new GradientColorKey(new Color(0.86f, 0.86f, 0.9f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.04f), new GradientAlphaKey(0.7f, 0.4f), new GradientAlphaKey(0f, 1f) });
            var overLife = system.colorOverLifetime;
            overLife.enabled = true;
            overLife.color = new ParticleSystem.MinMaxGradient(fade);

            var spin = system.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);

            SmokeSheet.Apply(system);

            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = settings.MissileSmokeMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.maxParticleSize = 2f;
        }
    }
}
