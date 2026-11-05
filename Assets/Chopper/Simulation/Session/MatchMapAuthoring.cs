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

        public byte MapId => mapId;

        /// <summary>Editor only: used by the setup tool.</summary>
        public void Set(byte id) => mapId = id;

        private sealed class Baker : Baker<MatchMapAuthoring>
        {
            public override void Bake(MatchMapAuthoring authoring)
            {
                AddComponent(GetEntity(TransformUsageFlags.None), new MatchMap { MapId = authoring.mapId });
            }
        }
    }
}
