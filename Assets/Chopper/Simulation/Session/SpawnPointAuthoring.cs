using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Chopper.Simulation.Session
{
    /// <summary>One place a chopper can spawn (gameplay subscene). The server picks the best one per spawn.</summary>
    public struct SpawnPoint : IComponentData
    {
        public float3 Position;
        public quaternion Rotation;
        /// <summary>Order in the map: the last tie-breaker, so the pick never depends on chunk order.</summary>
        public int Index;
    }

    /// <summary>Server: when the point was last used (world elapsed time, s).</summary>
    public struct SpawnPointUse : IComponentData
    {
        public double LastUsed;

        /// <summary>Never used.</summary>
        public const double Never = -1.0e9;
    }

    /// <summary>
    /// Put several in the gameplay subscene (Chopper → Setup makes a first set: step 4, or step 10 on city maps).
    /// The chopper spawns at this transform, with its facing. Keep it in open air, clear of walls and the ground:
    /// the server does not use a point that fails the <see cref="SpawnSettings"/> checks.
    /// </summary>
    public sealed class SpawnPointAuthoring : MonoBehaviour
    {
        [SerializeField, Tooltip("Order in the map (tie-breaker). Setup sets it.")] private int index;

        public int Index => index;

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetIndex(int value) => index = value;

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.22f, 1f, 0.53f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 4f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 14f);
        }

        private sealed class Baker : Baker<SpawnPointAuthoring>
        {
            public override void Bake(SpawnPointAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                var transform = authoring.transform;
                AddComponent(entity, new SpawnPoint
                {
                    Position = transform.position,
                    Rotation = transform.rotation,
                    Index = authoring.index,
                });
                AddComponent(entity, new SpawnPointUse { LastUsed = SpawnPointUse.Never });
            }
        }
    }
}
