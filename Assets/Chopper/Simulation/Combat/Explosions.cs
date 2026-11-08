using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;

namespace Chopper.Simulation.Combat
{
    /// <summary>Server-side splash damage shared by every explosive weapon (rockets, missiles, later bombs and clusters).</summary>
    public static class Explosions
    {
        /// <summary>
        /// Damage, falling off with distance, to every damageable body in the radius. The source (shooter) and the
        /// direct-hit target are skipped: the shooter takes no splash, the direct target already took its hit.
        /// </summary>
        public static void Splash(in CollisionWorld world, ref BufferLookup<DamageEvent> damageLookup, float3 centre, float radius,
            float damage, Entity source, Entity directTarget, in FixedString32Bytes weapon = default)
        {
            if (radius <= 0f || damage <= 0f) return;

            var hits = new NativeList<DistanceHit>(16, Allocator.Temp);
            var damaged = new NativeHashSet<Entity>(16, Allocator.Temp);
            if (world.OverlapSphere(centre, radius, ref hits, CollisionFilter.Default))
            {
                foreach (var hit in hits)
                {
                    var target = hit.Entity;
                    if (target == source || target == directTarget || !damaged.Add(target)) continue;
                    if (!damageLookup.TryGetBuffer(target, out var buffer)) continue;

                    var falloff = 1f - math.saturate(hit.Distance / radius);
                    // The closest point of the target to the blast: sparks on the side that faces it.
                    buffer.Add(new DamageEvent
                    {
                        Amount = damage * falloff,
                        Source = source,
                        Point = hit.Position,
                        Normal = SafeNormal(centre - hit.Position, hit.SurfaceNormal),
                        Kind = DamageKind.Explosion,
                        Weapon = weapon,
                    });
                }
            }
            hits.Dispose();
            damaged.Dispose();
        }

        /// <summary>Unit vector along <paramref name="direction"/>, or <paramref name="fallback"/> when it is (almost) zero.</summary>
        public static float3 SafeNormal(float3 direction, float3 fallback)
        {
            var length = math.length(direction);
            return length > 1e-4f ? direction / length : fallback;
        }

        /// <summary>Closest ray hit that is not <paramref name="ignore"/>.</summary>
        public static bool TryRaycast(in CollisionWorld world, Entity ignore, float3 start, float3 end, out RaycastHit closest)
        {
            var ray = new RaycastInput { Start = start, End = end, Filter = CollisionFilter.Default };
            var hits = new NativeList<RaycastHit>(8, Allocator.Temp);
            closest = default;
            var found = false;

            if (world.CastRay(ray, ref hits))
            {
                foreach (var hit in hits)
                {
                    if (hit.Entity == ignore || (found && hit.Fraction >= closest.Fraction)) continue;
                    closest = hit;
                    found = true;
                }
            }
            hits.Dispose();
            return found;
        }
    }
}
