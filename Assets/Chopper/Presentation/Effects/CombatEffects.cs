using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

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

        public CombatEffects(CombatEffectsSettings settings, Transform parent)
        {
            Settings = settings;
            _root = new GameObject("Combat Effects").transform;
            _root.SetParent(parent, false);
            _pool = new EffectPool(_root);
        }

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

        /// <summary>Pooled projectile visual. <paramref name="muteAudio"/> silences the prefab's own sounds.</summary>
        public GameObject SpawnPooled(GameObject prefab, Vector3 position, Quaternion rotation, bool muteAudio = false)
        {
            var instance = _pool.Spawn(prefab, position, rotation);
            if (instance != null) RouteAudio(instance, muteAudio);
            return instance;
        }

        /// <summary>Gives a pooled visual back, after <paramref name="seconds"/> so trails can fade.</summary>
        public void Release(GameObject instance, float seconds = 0f) => _pool.Release(instance, seconds);

        public void Tick(float deltaTime) => _pool.Tick(deltaTime);

        public void Dispose()
        {
            _pool.Dispose();
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
