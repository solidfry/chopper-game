using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Pool for looping effect objects (projectile visuals). Spawning restarts every particle system and clears trails;
    /// releasing can wait so smoke trails fade out instead of vanishing.
    /// Sounds with Play On Awake play again on every spawn (the object is enabled again).
    /// </summary>
    public sealed class EffectPool : IDisposable
    {
        private sealed class Entry
        {
            public GameObject Prefab;
            public ParticleSystem[] Particles;
            public TrailRenderer[] Trails;
            public AudioSource[] Audio;
            public Light[] Lights;
            public float ReleaseIn = -1f;
        }

        private readonly Transform _root;
        private readonly Dictionary<GameObject, Stack<GameObject>> _free = new();
        private readonly Dictionary<GameObject, Entry> _entries = new();
        private readonly List<GameObject> _releasing = new();

        public EffectPool(Transform root)
        {
            _root = root;
        }

        /// <summary>Returns null when <paramref name="prefab"/> is null.</summary>
        public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null) return null;

            GameObject instance = null;
            if (_free.TryGetValue(prefab, out var stack))
            {
                // Skip anything destroyed from outside (scene unload).
                while (stack.Count > 0 && instance == null)
                    instance = stack.Pop();
            }

            if (instance == null)
            {
                instance = Object.Instantiate(prefab, position, rotation, _root);
                _entries[instance] = new Entry
                {
                    Prefab = prefab,
                    Particles = instance.GetComponentsInChildren<ParticleSystem>(true),
                    Trails = instance.GetComponentsInChildren<TrailRenderer>(true),
                    Audio = instance.GetComponentsInChildren<AudioSource>(true),
                    Lights = instance.GetComponentsInChildren<Light>(true),
                };
                return instance;
            }

            instance.transform.SetPositionAndRotation(position, rotation);
            instance.SetActive(true);
            var entry = _entries[instance];
            entry.ReleaseIn = -1f;
            foreach (var trail in entry.Trails) trail.Clear();
            foreach (var light in entry.Lights) light.enabled = true;
            foreach (var particles in entry.Particles)
            {
                particles.Clear(withChildren: false);
                particles.Play(withChildren: false);
            }
            return instance;
        }

        /// <summary>Stops emitting now, takes the object back after <paramref name="seconds"/> (0 = at once).</summary>
        public void Release(GameObject instance, float seconds = 0f)
        {
            if (instance == null || !_entries.TryGetValue(instance, out var entry)) return;
            if (seconds <= 0f)
            {
                Return(instance, entry);
                return;
            }

            // The trail fades out; light and sound stop now.
            foreach (var particles in entry.Particles)
                particles.Stop(withChildren: false, ParticleSystemStopBehavior.StopEmitting);
            foreach (var audio in entry.Audio) audio.Stop();
            foreach (var light in entry.Lights) light.enabled = false;
            if (!_releasing.Contains(instance)) _releasing.Add(instance);
            entry.ReleaseIn = seconds;
        }

        public void Tick(float deltaTime)
        {
            for (var i = _releasing.Count - 1; i >= 0; i--)
            {
                var instance = _releasing[i];
                // Destroyed, or already returned / spawned again since the release was asked for.
                if (instance == null || !_entries.TryGetValue(instance, out var entry) || entry.ReleaseIn < 0f)
                {
                    _releasing.RemoveAt(i);
                    continue;
                }

                entry.ReleaseIn -= deltaTime;
                if (entry.ReleaseIn > 0f) continue;
                _releasing.RemoveAt(i);
                Return(instance, entry);
            }
        }

        public void Dispose()
        {
            foreach (var instance in _entries.Keys)
                if (instance != null) Object.Destroy(instance);
            _entries.Clear();
            _free.Clear();
            _releasing.Clear();
        }

        private void Return(GameObject instance, Entry entry)
        {
            entry.ReleaseIn = -1f;
            instance.SetActive(false);
            if (!_free.TryGetValue(entry.Prefab, out var stack))
                _free[entry.Prefab] = stack = new Stack<GameObject>();
            stack.Push(instance);
        }
    }
}
