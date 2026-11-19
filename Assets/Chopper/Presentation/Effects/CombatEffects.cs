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
        public void SpawnOneShot(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null || Settings == null) return;
            var instance = Object.Instantiate(prefab, position, rotation, _root);
            RouteAudio(instance, mute: false);
            Object.Destroy(instance, Settings.OneShotLifetime);
        }

        /// <summary>
        /// One explosion layer: scaled (particle sizes and speeds follow the transform), emits for its seconds, then is
        /// destroyed after its life. Not pooled (rare: one per chopper death).
        /// </summary>
        public void SpawnLayer(in BlastLayer layer, Vector3 position)
        {
            if (layer.Prefab == null || Settings == null) return;
            var offset = Vector3.up * layer.Up;
            if (layer.Spread > 0f)
            {
                var side = Random.insideUnitCircle * layer.Spread;
                offset += new Vector3(side.x, Random.Range(-0.3f, 0.6f) * layer.Spread, side.y);
            }
            var instance = Object.Instantiate(layer.Prefab, position + offset, Random.rotation, _root);
            var grows = layer.GrowSeconds > 0f && layer.StartScale > 0f;
            instance.transform.localScale = Vector3.one * (grows ? layer.StartScale : layer.Scale);
            if (grows) _growing.Add((instance, _clock, layer.StartScale, layer.Scale, layer.GrowSeconds));
            instance.GetComponentsInChildren(true, _systemBuffer);
            foreach (var system in _systemBuffer)
            {
                var main = system.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
            _systemBuffer.Clear();
            RouteAudio(instance, mute: false);
            if (layer.EmitSeconds > 0f) _emitting.Add((_clock + layer.EmitSeconds, instance));
            Object.Destroy(instance, layer.LifeSeconds);
        }

        /// <summary>Pooled projectile visual. <paramref name="muteAudio"/> silences the prefab's own sounds.</summary>
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
