using Unity.Entities;
using UnityEngine;

namespace Chopper.Simulation.Visuals
{
    /// <summary>
    /// Optional: put it on a GameObject with a collider to set what the collider is made of (for example a metal shed:
    /// its boxes would count as concrete). Without it the surface comes from the collider (<see cref="Surfaces.Classify"/>).
    /// Lives in its own file: Unity links a MonoBehaviour to its script only when the file name matches the class.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SurfaceAuthoring : MonoBehaviour
    {
        [SerializeField] private SurfaceKind kind = SurfaceKind.Concrete;

        public SurfaceKind Kind
        {
            get => kind;
            set => kind = value;
        }

        private sealed class Baker : Baker<SurfaceAuthoring>
        {
            public override void Bake(SurfaceAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new PhysicsSurface { Kind = authoring.kind });
            }
        }
    }
}
