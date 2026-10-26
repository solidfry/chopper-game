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

        private EffectPool _pool;

        public CombatEffectsSettings Settings => settings;
        public EffectPool Pool => _pool ??= new EffectPool(transform);

        /// <summary>Muzzle flashes and impacts. Not pooled: the pack's light fade script destroys its light.</summary>
        public void SpawnOneShot(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null || settings == null) return;
            var instance = Instantiate(prefab, position, rotation, transform);
            Destroy(instance, settings.OneShotLifetime);
        }

        /// <summary>Editor only: used by the scene setup tool.</summary>
        public void SetSettings(CombatEffectsSettings value) => settings = value;

        private void Update() => _pool?.Tick(Time.deltaTime);

        private void OnDestroy() => _pool?.Dispose();
    }
}
