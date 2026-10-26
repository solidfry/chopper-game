using System.Collections.Generic;
using UnityEngine;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Scene owner of the combat effects: settings plus the pool and one-shot spawner they share. The effect systems
    /// find it in the scene. TODO: register in the match <c>LifetimeScope</c> (VContainer) instead.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatEffectsHost : MonoBehaviour
    {
        [SerializeField] private CombatEffectsSettings settings;

        private readonly List<AudioSource> _audioBuffer = new();
        private EffectPool _pool;

        public CombatEffectsSettings Settings => settings;
        public EffectPool Pool => _pool ??= new EffectPool(transform);

        /// <summary>Muzzle flashes and impacts. Not pooled: the pack's light fade script destroys its light.</summary>
        public void SpawnOneShot(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null || settings == null) return;
            var instance = Instantiate(prefab, position, rotation, transform);
            RouteAudio(instance, mute: false);
            Destroy(instance, settings.OneShotLifetime);
        }

        /// <summary>Pooled projectile visual. <paramref name="muteAudio"/> silences the prefab's own sounds.</summary>
        public GameObject SpawnPooled(GameObject prefab, Vector3 position, Quaternion rotation, bool muteAudio = false)
        {
            var instance = Pool.Spawn(prefab, position, rotation);
            if (instance != null) RouteAudio(instance, muteAudio);
            return instance;
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
                if (settings.EffectsMixerGroup != null)
                    source.outputAudioMixerGroup = settings.EffectsMixerGroup;
            }
            _audioBuffer.Clear();
        }

        /// <summary>Editor only: used by the scene setup tool.</summary>
        public void SetSettings(CombatEffectsSettings value) => settings = value;

        private void Update() => _pool?.Tick(Time.deltaTime);

        private void OnDestroy() => _pool?.Dispose();
    }
}
