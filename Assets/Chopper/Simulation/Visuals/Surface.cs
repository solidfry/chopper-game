using Unity.Entities;
using Unity.Physics;
using UnityEngine;

namespace Chopper.Simulation.Visuals
{
    /// <summary>What a collider is made of, for impact sounds (and later impact effects). Cosmetic only.</summary>
    public enum SurfaceKind : byte
    {
        /// <summary>Buildings, roads, roofs: the default for static boxes and convex shapes.</summary>
        Concrete = 0,
        /// <summary>Choppers, targets, cranes, industrial plant: every moving body.</summary>
        Metal = 1,
        /// <summary>Terrain: mesh and terrain colliders.</summary>
        Ground = 2,
        /// <summary>Colliders on the Unity "Water" layer.</summary>
        Water = 3,
    }

    /// <summary>A collider's surface, set by hand (<see cref="SurfaceAuthoring"/>). Without it, <see cref="Surfaces.Classify"/> guesses.</summary>
    public struct PhysicsSurface : IComponentData
    {
        public SurfaceKind Kind;
    }

    /// <summary>The surface at a ray or overlap hit. Shared by every impact sound, so they all agree.</summary>
    public static class Surfaces
    {
        /// <summary>The built-in "Water" layer. Built-in colliders belong to their GameObject's layer.</summary>
        private const int WaterLayer = 4;

        /// <summary>
        /// In order: a <see cref="PhysicsSurface"/> on the hit entity; any moving body (chopper, target, missile) = metal;
        /// the Water layer = water; mesh or terrain collider = ground (the city terrain chunks); anything else = concrete
        /// (the city's building boxes).
        /// </summary>
        public static SurfaceKind Classify(in CollisionWorld world, in Unity.Physics.RaycastHit hit, in ComponentLookup<PhysicsSurface> marked) =>
            Classify(world, hit.RigidBodyIndex, hit.Entity, marked);

        public static SurfaceKind Classify(in CollisionWorld world, int bodyIndex, Entity entity, in ComponentLookup<PhysicsSurface> marked)
        {
            if (marked.TryGetComponent(entity, out var surface)) return surface.Kind;
            if (bodyIndex < 0 || bodyIndex >= world.NumBodies) return SurfaceKind.Concrete;
            if (bodyIndex < world.NumDynamicBodies) return SurfaceKind.Metal;

            var collider = world.Bodies[bodyIndex].Collider;
            if (!collider.IsCreated) return SurfaceKind.Concrete;
            if ((collider.Value.GetCollisionFilter().BelongsTo & (1u << WaterLayer)) != 0) return SurfaceKind.Water;
            return collider.Value.Type is ColliderType.Mesh or ColliderType.Terrain ? SurfaceKind.Ground : SurfaceKind.Concrete;
        }
    }
}
