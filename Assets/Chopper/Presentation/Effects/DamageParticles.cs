using System;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// The damage look's four script-emitted particle systems (sparks, flames, smoke, crash dust) and how each one
    /// emits. Shared by the game (<see cref="DamageEffectsSystem"/>: hits, damaged and burning choppers, debris trails,
    /// repair and status looks) and the effects bench (<see cref="EffectsBench"/>). A system whose material is not set
    /// is null, and its emits do nothing.
    /// </summary>
    public sealed class DamageParticles : IDisposable
    {
        /// <summary>Share of the chopper's velocity that smoke keeps (it trails, it does not hang still).</summary>
        public const float SmokeInherit = 0.35f;

        private readonly Transform _root;
        private readonly CombatEffectsSettings _settings;

        public DamageParticles(CombatEffectsSettings settings, Transform parent = null)
        {
            _settings = settings;
            _root = new GameObject("Damage Effects").transform;
            if (parent != null) _root.SetParent(parent, false);
            if (settings == null) return;
            if (settings.DamageSparkMaterial != null)
            {
                Sparks = Create("Sparks", settings.DamageSparkMaterial, 1500, ParticleSystemRenderMode.Stretch, gravity: 1f,
                    Fade(new Color(1f, 0.95f, 0.75f), new Color(1f, 0.45f, 0.1f), 0.7f), grow: 0.6f, drag: 0.02f);
                Flames = Create("Flames", settings.DamageFlameMaterial, 600, ParticleSystemRenderMode.Billboard, gravity: -0.2f,
                    Fade(new Color(1f, 0.75f, 0.3f), new Color(0.9f, 0.2f, 0.05f), 0.25f), grow: 1.6f, drag: 0.1f);
                if (settings.FlameFlipbook)
                {
                    // The fireball flipbook: all 16 frames over the life (it flares, then breaks up).
                    var sheet = Flames.textureSheetAnimation;
                    sheet.enabled = true;
                    sheet.mode = ParticleSystemAnimationMode.Grid;
                    sheet.numTilesX = 4;
                    sheet.numTilesY = 4;
                    sheet.animation = ParticleSystemAnimationType.WholeSheet;
                    sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
                    sheet.cycleCount = 1;
                }
            }
            if (settings.DamageSmokeMaterial != null)
                Smoke = Create("Smoke", settings.DamageSmokeMaterial, 2000, ParticleSystemRenderMode.Billboard, gravity: -0.03f,
                    Fade(Color.white, Color.white, 0.6f), grow: 3f, drag: 0.08f, sheet: true);
            if (settings.DownwashMaterial != null)
                Dust = Create("Crash Dust", settings.DownwashMaterial, 400, ParticleSystemRenderMode.Billboard, gravity: 0.05f,
                    Fade(Color.white, Color.white, 0.5f), grow: 2f, drag: 0.12f, sheet: true);
        }

        /// <summary>Null when the settings have no spark material (also used for repair, heal and status looks).</summary>
        public ParticleSystem Sparks { get; }
        public ParticleSystem Flames { get; }
        public ParticleSystem Smoke { get; }
        public ParticleSystem Dust { get; }

        /// <summary>A hit: sparks in a cone round <paramref name="normal"/>, some skimming along the hull.</summary>
        public void EmitSparks(Vector3 point, Vector3 normal, int count)
        {
            if (Sparks == null || count <= 0) return;
            var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (var i = 0; i < count; i++)
            {
                var direction = (normal + Random.insideUnitSphere * 0.9f).normalized;
                emit.position = point + normal * 0.05f;
                emit.velocity = direction * (_settings.SparkSpeed * Random.Range(0.35f, 1.1f));
                emit.startLifetime = Random.Range(0.25f, 0.7f);
                emit.startSize = Random.Range(0.05f, 0.12f);
                Sparks.Emit(emit, 1);
            }
        }

        public void EmitSmoke(Vector3 point, Vector3 normal, Vector3 velocity, Color colour, float strength, int count)
        {
            if (Smoke == null || count <= 0) return;
            var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false, startColor = colour };
            for (var i = 0; i < count; i++)
            {
                emit.position = point + Random.insideUnitSphere * 0.25f;
                emit.velocity = velocity * SmokeInherit + normal * Random.Range(0.5f, 1.5f) + Vector3.up * Random.Range(0.5f, 1.5f);
                emit.startLifetime = Random.Range(2f, 3.5f) * (0.6f + 0.4f * strength);
                emit.startSize = _settings.SmokeSize * Random.Range(0.7f, 1.3f) * (0.6f + 0.4f * strength);
                emit.rotation = Random.Range(0f, 360f);
                Smoke.Emit(emit, 1);
            }
        }

        public void EmitFlames(Vector3 point, Vector3 normal, Vector3 velocity, float strength, int count)
        {
            if (Flames == null || count <= 0) return;
            var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (var i = 0; i < count; i++)
            {
                // Longer, bigger tongues (owner, 20 Nov: "more visually appealing").
                emit.position = point + Random.insideUnitSphere * 0.3f;
                emit.velocity = velocity * 0.6f + normal * Random.Range(0.5f, 2f) + Vector3.up * Random.Range(1.5f, 3.5f);
                emit.startLifetime = Random.Range(0.3f, 0.7f);
                emit.startSize = Random.Range(1.5f, 3f) * (0.5f + 0.5f * strength);
                emit.rotation = Random.Range(0f, 360f);
                Flames.Emit(emit, 1);
                // Some flames throw an ember that rises and drifts.
                if (Random.value < 0.35f) EmitEmber(point, velocity);
            }
        }

        private void EmitEmber(Vector3 point, Vector3 velocity)
        {
            if (Sparks == null) return;
            var emit = new ParticleSystem.EmitParams
            {
                applyShapeToPosition = false,
                position = point + Random.insideUnitSphere * 0.4f,
                velocity = velocity * 0.5f + Vector3.up * Random.Range(3f, 7f) + Random.insideUnitSphere * 2f,
                startLifetime = Random.Range(0.8f, 1.6f),
                startSize = Random.Range(0.04f, 0.09f),
            };
            Sparks.Emit(emit, 1);
        }

        /// <summary>Crash dust thrown out round <paramref name="point"/> in <paramref name="colour"/> (the map's sand or ash).</summary>
        public void EmitDust(Vector3 point, Color colour, int count)
        {
            if (Dust == null || count <= 0) return;
            var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false, startColor = colour };
            for (var i = 0; i < count; i++)
            {
                var outward = Random.insideUnitCircle.normalized;
                emit.position = point + new Vector3(outward.x, 0.2f, outward.y) * 0.5f;
                emit.velocity = new Vector3(outward.x, Random.Range(0.2f, 0.6f), outward.y) * Random.Range(4f, 10f);
                emit.startLifetime = Random.Range(1f, 2.2f);
                emit.startSize = Random.Range(1.5f, 3f);
                emit.rotation = Random.Range(0f, 360f);
                Dust.Emit(emit, 1);
            }
        }

        /// <summary>Debris trails (<see cref="WreckDebris.SetTrails"/>): heavy smoke and flames going up, sparks along the flight.</summary>
        public void DebrisSmoke(Vector3 point, Vector3 velocity, float strength, int count) =>
            EmitSmoke(point, Vector3.up, velocity, _settings.HeavySmokeColour, strength, count);

        public void DebrisFlames(Vector3 point, Vector3 velocity, float strength, int count) =>
            EmitFlames(point, Vector3.up, velocity, strength, count);

        public void DebrisSparks(Vector3 point, Vector3 velocity, float strength, int count)
        {
            var normal = velocity.sqrMagnitude > 0.01f ? velocity.normalized : Vector3.up;
            EmitSparks(point, normal, Mathf.RoundToInt(count * strength));
        }

        public void Dispose()
        {
            if (_root != null) Object.Destroy(_root.gameObject);
        }

        /// <summary>Colour over lifetime: <paramref name="start"/> to <paramref name="end"/>, alpha up fast, held, then out.</summary>
        private static Gradient Fade(Color start, Color end, float hold)
        {
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(start, 0f), new GradientColorKey(end, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.05f), new GradientAlphaKey(0.8f, hold), new GradientAlphaKey(0f, 1f) });
            return gradient;
        }

        /// <summary>A world-space system that only emits by script (Emit), with drag, growth and a colour fade.
        /// <paramref name="sheet"/>: the material uses the smoke flipbook (<see cref="SmokeSheet"/>).</summary>
        private ParticleSystem Create(string name, Material material, int maxParticles, ParticleSystemRenderMode mode, float gravity,
            Gradient colour, float grow, float drag, bool sheet = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (sheet) SmokeSheet.Apply(system);

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
    }
}
