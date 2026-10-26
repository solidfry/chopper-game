using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Chopper.Simulation.Session
{
    public struct ChopperSpawner : IComponentData
    {
        public Entity Prefab;
        public float3 Position;
        public quaternion Rotation;
        /// <summary>Sideways gap between players so they do not spawn inside each other.</summary>
        public float Spacing;
        /// <summary>Seconds between death and the new chopper.</summary>
        public float RespawnDelay;
    }

    /// <summary>Put in the gameplay subscene. Its transform is the first spawn point.</summary>
    public sealed class ChopperSpawnerAuthoring : MonoBehaviour
    {
        [SerializeField] private GameObject chopperPrefab;
        [SerializeField] private float spacing = 20f;
        [SerializeField] private float respawnDelay = 3f;

        private sealed class Baker : Baker<ChopperSpawnerAuthoring>
        {
            public override void Bake(ChopperSpawnerAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                var transform = authoring.transform;
                AddComponent(entity, new ChopperSpawner
                {
                    Prefab = GetEntity(authoring.chopperPrefab, TransformUsageFlags.Dynamic),
                    Position = transform.position,
                    Rotation = transform.rotation,
                    Spacing = authoring.spacing,
                    RespawnDelay = authoring.respawnDelay,
                });
            }
        }
    }
}
