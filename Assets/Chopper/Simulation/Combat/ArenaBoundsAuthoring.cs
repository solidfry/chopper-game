using Unity.Entities;
using UnityEngine;

namespace Chopper.Simulation.Combat
{
    /// <summary>Put one in the gameplay subscene. The box is centred on this transform (rotation is ignored).</summary>
    [DisallowMultipleComponent]
    public sealed class ArenaBoundsAuthoring : MonoBehaviour
    {
        [SerializeField] private Vector3 size = new(2000f, 600f, 2000f);
        [SerializeField] private float damagePerSecond = 15f;

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.22f, 1f, 0.53f, 0.6f);
            Gizmos.DrawWireCube(transform.position, size);
        }

        private sealed class Baker : Baker<ArenaBoundsAuthoring>
        {
            public override void Bake(ArenaBoundsAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new ArenaBounds
                {
                    Center = authoring.transform.position,
                    HalfExtents = authoring.size * 0.5f,
                    DamagePerSecond = authoring.damagePerSecond,
                });
            }
        }
    }
}
