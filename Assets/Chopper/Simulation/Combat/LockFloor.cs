using Chopper.Simulation.Visuals;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;

namespace Chopper.Simulation.Combat
{
    /// <summary>
    /// Missile lock floor (owner, 28 Nov 2026): an air target lower than <see cref="MinHeight"/> over the ground cannot be
    /// locked, and the seeker marker shows a cross on it. Height counts down to the ground or water only: a roof or any
    /// other building does not count, so a chopper on a rooftop is still too low if the street is within the floor.
    /// </summary>
    public static class LockFloor
    {
        /// <summary>Metres over the ground (or water) below which an air target cannot be locked.</summary>
        public const float MinHeight = 40f;

        /// <summary>True when ground or water lies less than <see cref="MinHeight"/> below <paramref name="position"/>.</summary>
        public static bool TooLow(in CollisionWorld world, in ComponentLookup<PhysicsSurface> surfaces, Entity self, float3 position)
        {
            var ray = new RaycastInput
            {
                Start = position,
                End = position - new float3(0f, MinHeight, 0f),
                Filter = CollisionFilter.Default,
            };
            var hits = new NativeList<RaycastHit>(8, Allocator.Temp);
            var low = false;
            if (world.CastRay(ray, ref hits))
                foreach (var hit in hits)
                {
                    if (hit.Entity == self) continue;
                    var kind = Surfaces.Classify(world, hit, surfaces);
                    if (kind == SurfaceKind.Ground || kind == SurfaceKind.Water)
                    {
                        low = true;
                        break;
                    }
                }
            hits.Dispose();
            return low;
        }
    }
}
