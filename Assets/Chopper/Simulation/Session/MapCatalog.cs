using System;
using System.Collections.Generic;
using Chopper.Simulation.Config;
using UnityEngine;

namespace Chopper.Simulation.Session
{
    /// <summary>
    /// Every playable map: its id (sent over the network), name, and scene. Training lets the player pick from the
    /// <see cref="MapEntry.InTraining"/> maps (hangar); a hosted match gets a random <see cref="MapEntry.InMatchPool"/>
    /// map (the server boot scene, <see cref="ServerMapLoader"/>), and tells each client which (<see cref="MatchMap"/>).
    /// Setup step 10 adds the city maps; the GameConfig points to it.
    /// </summary>
    [CreateAssetMenu(menuName = "Chopper/Map Catalog", fileName = "MapCatalog")]
    public sealed class MapCatalog : ScriptableObject, IValidatedAsset
    {
        [Serializable]
        public sealed class MapEntry
        {
            [Tooltip("Network id: never change it once a build is out.")] public byte Id;
            public string DisplayName;
            [Tooltip("Scene name (in the build settings), e.g. Map_City04.")] public string Scene;
            [Tooltip("The player can pick it for training.")] public bool InTraining = true;
            [Tooltip("Matchmaking can pick it for an online match.")] public bool InMatchPool = true;
        }

        [SerializeField] private List<MapEntry> maps = new();

        public IReadOnlyList<MapEntry> Maps => maps;

        public MapEntry Find(byte id)
        {
            foreach (var map in maps) if (map != null && map.Id == id) return map;
            return null;
        }

        public MapEntry FindScene(string scene)
        {
            foreach (var map in maps)
                if (map != null && string.Equals(map.Scene, scene, StringComparison.OrdinalIgnoreCase)) return map;
            return null;
        }

        /// <summary>The training maps, in list order.</summary>
        public List<MapEntry> Training() => maps.FindAll(m => m != null && m.InTraining && !string.IsNullOrEmpty(m.Scene));

        /// <summary>A random match map (null when the pool is empty). <paramref name="roll"/> is 0..1.</summary>
        public MapEntry RandomMatchMap(float roll)
        {
            var pool = maps.FindAll(m => m != null && m.InMatchPool && !string.IsNullOrEmpty(m.Scene));
            if (pool.Count == 0) return null;
            return pool[Mathf.Clamp((int)(roll * pool.Count), 0, pool.Count - 1)];
        }

        /// <summary>Editor only: the setup tool adds a map it built (no change when the scene is listed).</summary>
        public MapEntry Add(string scene, string displayName, bool inTraining, bool inMatchPool)
        {
            var found = FindScene(scene);
            if (found != null) return found;
            byte next = 0;
            foreach (var map in maps) if (map != null && map.Id >= next) next = (byte)(map.Id + 1);
            var entry = new MapEntry { Id = next, DisplayName = displayName, Scene = scene, InTraining = inTraining, InMatchPool = inMatchPool };
            maps.Add(entry);
            return entry;
        }

        public void CollectProblems(List<string> problems)
        {
            if (maps.Count == 0) problems.Add($"{name}: no maps.");
            var ids = new HashSet<byte>();
            foreach (var map in maps)
            {
                if (map == null) continue;
                if (!ids.Add(map.Id)) problems.Add($"{name}: map id {map.Id} is used twice.");
                if (string.IsNullOrEmpty(map.Scene)) problems.Add($"{name}: map {map.Id} has no scene.");
            }
            if (!maps.Exists(m => m != null && m.InMatchPool)) problems.Add($"{name}: no map is in the match pool.");
        }
    }
}
