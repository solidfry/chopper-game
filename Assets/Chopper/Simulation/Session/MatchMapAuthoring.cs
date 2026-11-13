using Unity.Entities;
using UnityEngine;

namespace Chopper.Simulation.Session
{
    /// <summary>Marks a gameplay subscene with its map (<see cref="MapCatalog"/> id); bakes <see cref="MatchMap"/>.
    /// Own file: Unity links a MonoBehaviour to its script only when the file has the class's name.</summary>
    [DisallowMultipleComponent]
    public sealed class MatchMapAuthoring : MonoBehaviour
    {
        [SerializeField, Tooltip("Map id in the MapCatalog. Setup sets it.")] private byte mapId;
        [SerializeField, Tooltip("Allowed times of day (bit n = TimeOfDay n; 0 = Night). Setup copies them from the MapCatalog.")]
        private byte allowedTimes;
        [SerializeField, Tooltip("Allowed weathers (bit n = Weather n; 0 = Clear). Setup copies them from the MapCatalog.")]
        private byte allowedWeathers;

        public byte MapId => mapId;
        public byte AllowedTimes => allowedTimes;
        public byte AllowedWeathers => allowedWeathers;

        /// <summary>Editor only: used by the setup tool.</summary>
        public void Set(byte id, byte times = 0, byte weathers = 0)
        {
            mapId = id;
            allowedTimes = times;
            allowedWeathers = weathers;
        }

        private sealed class Baker : Baker<MatchMapAuthoring>
        {
            public override void Bake(MatchMapAuthoring authoring)
            {
                AddComponent(GetEntity(TransformUsageFlags.None), new MatchMap
                {
                    MapId = authoring.mapId, AllowedTimes = authoring.allowedTimes, AllowedWeathers = authoring.allowedWeathers,
                });
            }
        }
    }
}
