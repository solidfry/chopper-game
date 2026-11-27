using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Spawns combat effect prefabs: pooled projectile visuals and one-shot flashes/impacts. Routes the prefabs' own
    /// sounds to the Effects mixer group. Owned by <see cref="Bridge.PresentationContext"/>.
    /// </summary>
    public sealed class CombatEffects : IDisposable
    {
        private readonly Transform _root;
        private readonly EffectPool _pool;
        private readonly List<AudioSource> _audioBuffer = new();
        private readonly List<ParticleSystem> _systemBuffer = new();
        /// <summary>Layers still emitting, and when they stop.</summary>
        private readonly List<(float StopAt, GameObject Instance)> _emitting = new();
        /// <summary>Layers still growing: start time, start and end scale, seconds.</summary>
        private readonly List<(GameObject Instance, float Start, float From, float To, float Seconds)> _growing = new();
        private float _clock;

        public CombatEffects(CombatEffectsSettings settings, Transform parent)
        {
            Settings = settings;
            _root = new GameObject("Combat Effects").transform;
            _root.SetParent(parent, false);
            _pool = new EffectPool(_root);
            Debris = new WreckDebris(_root);
        }

        /// <summary>The wreck break-up pieces (Docs/ChopperDeathDesign.md step 2).</summary>
        public WreckDebris Debris { get; }

        /// <summary>Can be null (no effects configured): every spawn then does nothing.</summary>
        public CombatEffectsSettings Settings { get; }

        /// <summary>Muzzle flashes and impacts. Not pooled: the pack's light fade script destroys its light.</summary>
        /// <param name="muteAudio">True when a sound cue plays instead of the prefab's own audio (missile and rocket blasts).</param>
        public void SpawnOneShot(GameObject prefab, Vector3 position, Quaternion rotation, bool muteAudio = false)
        {
            if (prefab == null || Settings == null) return;
            var instance = Object.Instantiate(prefab, position, rotation, _root);
            RouteAudio(instance, muteAudio);
            Object.Destroy(instance, Settings.OneShotLifetime);
        }

        /// <summary>
        /// One explosion layer: scaled (particle sizes and speeds follow the transform), emits for its seconds, then is
        /// destroyed after its life. Not pooled (rare: one per chopper death).
        /// </summary>
        /// <param name="muteAudio">True when a sound cue plays instead of the prefab's own audio (the wreck blast).</param>
        /// <summary>Every layer at once, at <paramref name="position"/> (a weapon look's impact layers).</summary>
        public void SpawnLayers(System.Collections.Generic.IReadOnlyList<BlastLayer> layers, Vector3 position, bool muteAudio = false)
        {
            if (layers == null) return;
            for (var i = 0; i < layers.Count; i++) SpawnLayer(layers[i], position, muteAudio);
        }

        public void SpawnLayer(in BlastLayer layer, Vector3 position, bool muteAudio = false)
        {
            if (layer.Prefab == null || Settings == null) return;
            var offset = Vector3.up * layer.Up;
            if (layer.Spread > 0f)
            {
                var side = Random.insideUnitCircle * layer.Spread;
                offset += new Vector3(side.x, Random.Range(-0.3f, 0.6f) * layer.Spread, side.y);
            }
            // Upright, random heading only: the fire and smoke packs rise along the prefab's up axis, and their flame
            // meshes are drawn upright. A full random rotation sent smoke sideways and turned flames upside down.
            var heading = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            var instance = Object.Instantiate(layer.Prefab, position + offset, heading, _root);
            var grows = layer.GrowSeconds > 0f && layer.StartScale > 0f;
            instance.transform.localScale = Vector3.one * (grows ? layer.StartScale : layer.Scale);
            if (grows) _growing.Add((instance, _clock, layer.StartScale, layer.Scale, layer.GrowSeconds));
            instance.GetComponentsInChildren(true, _systemBuffer);
            foreach (var system in _systemBuffer)
            {
                var main = system.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                if (layer.Tint.a > 0f) main.startColor = Tinted(main.startColor, layer.Tint);
                if (layer.LifeColour.a > 0f && system.colorOverLifetime.enabled)
                {
                    var colour = system.colorOverLifetime;
                    var gradient = new Gradient();
                    gradient.SetKeys(
                        new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(layer.LifeColour, 0.5f), new GradientColorKey(Color.black, 1f) },
                        new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
                    colour.color = gradient;
                }
            }
            _systemBuffer.Clear();
            foreach (var renderer in instance.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                if (layer.Material != null) renderer.sharedMaterial = layer.Material;
                // Stretched billboards with no speed or camera stretch (the NatureManufacture smoke) only turn each puff
                // to face its own motion: puffs flying at the camera fold into long, bent strips. A billboard looks the same
                // from the side and stays round from the front.
                if (renderer.renderMode == ParticleSystemRenderMode.Stretch && renderer.velocityScale <= 0f &&
                    renderer.cameraVelocityScale <= 0f)
                {
                    renderer.renderMode = ParticleSystemRenderMode.Billboard;
                    FlattenRotation(renderer);
                }
                else if (renderer.renderMode == ParticleSystemRenderMode.Mesh && IsFlatCard(renderer.mesh))
                {
                    FaceCamera(renderer);
                    FlattenRotation(renderer);
                }
                // Vertical billboards turn round the up axis only: seen from a chopper above they are thin slivers.
                else if (renderer.renderMode == ParticleSystemRenderMode.VerticalBillboard)
                {
                    renderer.renderMode = ParticleSystemRenderMode.Billboard;
                    FlattenRotation(renderer);
                }
            }
            RouteAudio(instance, muteAudio);
            if (layer.EmitSeconds > 0f) _emitting.Add((_clock + layer.EmitSeconds, instance));
            Object.Destroy(instance, layer.LifeSeconds);
        }

        /// <summary>The start colour times <paramref name="tint"/> (RGB only: the alpha stays the prefab's).</summary>
        private static ParticleSystem.MinMaxGradient Tinted(ParticleSystem.MinMaxGradient colour, Color tint)
        {
            var rgb = new Color(tint.r, tint.g, tint.b, 1f);
            return colour.mode switch
            {
                ParticleSystemGradientMode.TwoColors => new ParticleSystem.MinMaxGradient(colour.colorMin * rgb, colour.colorMax * rgb),
                ParticleSystemGradientMode.Color => new ParticleSystem.MinMaxGradient(colour.color * rgb),
                _ => new ParticleSystem.MinMaxGradient(rgb),
            };
        }

        /// <summary>A flat card (the pack's "particle plane" is a quad split 8 x 8): no depth on one axis.</summary>
        private static bool IsFlatCard(Mesh mesh)
        {
            if (mesh == null) return false;
            var size = mesh.bounds.size;
            var largest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            var smallest = Mathf.Min(size.x, Mathf.Min(size.y, size.z));
            return largest > 0f && smallest <= largest * 0.01f;
        }

        /// <summary>
        /// A flat card drawn as a mesh particle (the pack's big flames: a quad with "Facing" alignment). Unity turns mesh
        /// particles once per frame for one camera, not per camera, so the cards could face away from the player (owner:
        /// "they face the other choppers"). A billboard is built for each camera. The mesh's pivot offset (along its
        /// own z, its up once turned) becomes the billboard's up offset.
        /// </summary>
        private static void FaceCamera(ParticleSystemRenderer renderer)
        {
            var pivot = renderer.pivot;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.pivot = new Vector3(pivot.x, pivot.z, 0f);
        }

        /// <summary>
        /// A billboard made from a stretched or mesh particle keeps only its spin in the screen plane (Z). The packs set
        /// 3D start rotation for their own modes, where X and Y do no harm (the smoke column: Y 0-180 degrees; the
        /// flames: X 90). A billboard applies all three, so those cards turned edge-on (owner: "long slim ones") or lay
        /// flat (the red patch seen from above).
        /// </summary>
        private static void FlattenRotation(ParticleSystemRenderer renderer)
        {
            if (!renderer.TryGetComponent<ParticleSystem>(out var system)) return;
            var main = system.main;
            if (main.startRotation3D)
            {
                var spin = main.startRotationZ;
                main.startRotation3D = false;
                main.startRotation = spin;
            }
            var rotation = system.rotationOverLifetime;
            if (rotation.enabled && rotation.separateAxes)
            {
                var spin = rotation.z;
                rotation.separateAxes = false;
                rotation.z = spin;
            }
        }

        /// <summary>Pooled projectile visual. <paramref name="muteAudio"/> silences the prefab's own sounds.</summary>
        /// <summary>
        /// A gun tracer: pooled, and each particle system emits one particle at once. The pack's tracers emit by time
        /// only (10-20 a second), so a fast round showed nothing for its first 0.05-0.1 s: 45 m out, or not at all for a
        /// short cannon shot (owner, 27 Nov).
        /// </summary>
        public GameObject SpawnTracer(GameObject prefab, Vector3 position, Quaternion rotation, bool muteAudio = false, float scale = 1f,
            float brightness = 1f)
        {
            var instance = SpawnPooled(prefab, position, rotation, muteAudio);
            if (instance == null) return null;
            // Set on every spawn: the object comes from a pool.
            instance.transform.localScale = Vector3.one * scale;
            instance.GetComponentsInChildren(true, _systemBuffer);
            if (!Mathf.Approximately(brightness, 1f) || _brightened.Contains(instance))
            {
                // From the prefab's colours each time, so a pooled tracer never compounds.
                prefab.GetComponentsInChildren(true, _prefabSystems);
                for (var i = 0; i < _systemBuffer.Count && i < _prefabSystems.Count; i++)
                {
                    var main = _systemBuffer[i].main;
                    main.startColor = Brighten(_prefabSystems[i].main.startColor, brightness);
                }
                _prefabSystems.Clear();
                _brightened.Add(instance);
            }
            foreach (var system in _systemBuffer)
                if (system.gameObject.activeInHierarchy) system.Emit(1);
            _systemBuffer.Clear();
            return instance;
        }

        private readonly List<ParticleSystem> _prefabSystems = new();
        private readonly HashSet<GameObject> _brightened = new();

        /// <summary>Colour and opacity times <paramref name="factor"/> (opacity capped at 1). Gradients are left as made.</summary>
        private static ParticleSystem.MinMaxGradient Brighten(ParticleSystem.MinMaxGradient colour, float factor)
        {
            static Color Scale(Color c, float f) => new(c.r * f, c.g * f, c.b * f, Mathf.Min(1f, c.a * f));
            return colour.mode switch
            {
                ParticleSystemGradientMode.Color => new ParticleSystem.MinMaxGradient(Scale(colour.color, factor)),
                ParticleSystemGradientMode.TwoColors => new ParticleSystem.MinMaxGradient(Scale(colour.colorMin, factor), Scale(colour.colorMax, factor)),
                _ => colour,
            };
        }

        public GameObject SpawnPooled(GameObject prefab, Vector3 position, Quaternion rotation, bool muteAudio = false)
        {
            var instance = _pool.Spawn(prefab, position, rotation);
            if (instance != null) RouteAudio(instance, muteAudio);
            return instance;
        }

        /// <summary>Gives a pooled visual back, after <paramref name="seconds"/> so trails can fade.</summary>
        public void Release(GameObject instance, float seconds = 0f) => _pool.Release(instance, seconds);

        public void Tick(float deltaTime)
        {
            _pool.Tick(deltaTime);
            _clock += deltaTime;
            for (var i = _growing.Count - 1; i >= 0; i--)
            {
                var (instance, start, from, to, seconds) = _growing[i];
                var t = Mathf.Clamp01((_clock - start) / seconds);
                if (instance != null) instance.transform.localScale = Vector3.one * Mathf.Lerp(from, to, 1f - (1f - t) * (1f - t) * (1f - t));
                if (instance == null || t >= 1f) _growing.RemoveAt(i);
            }
            for (var i = _emitting.Count - 1; i >= 0; i--)
            {
                if (_emitting[i].StopAt > _clock) continue;
                var instance = _emitting[i].Instance;
                _emitting.RemoveAt(i);
                if (instance == null) continue;
                instance.GetComponentsInChildren(true, _systemBuffer);
                foreach (var system in _systemBuffer) system.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                _systemBuffer.Clear();
            }
        }

        public void Dispose()
        {
            _pool.Dispose();
            Debris.Dispose();
            if (_root != null) Object.Destroy(_root.gameObject);
        }

        /// <summary>Sends the prefab's sounds to the Effects mixer group, so the effects volume setting covers them.</summary>
        private void RouteAudio(GameObject instance, bool mute)
        {
            instance.GetComponentsInChildren(true, _audioBuffer);
            foreach (var source in _audioBuffer)
            {
                if (mute)
                {
                    source.Stop();
                    source.enabled = false;
                    continue;
                }
                source.enabled = true;
                if (Settings.EffectsMixerGroup != null)
                    source.outputAudioMixerGroup = Settings.EffectsMixerGroup;
            }
            _audioBuffer.Clear();
        }
    }
}
