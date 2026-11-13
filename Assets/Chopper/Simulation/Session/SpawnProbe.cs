using Chopper.Simulation.Bots;
using Chopper.Simulation.Combat;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Physics;

namespace Chopper.Simulation.Session
{
    /// <summary>A live chopper (or a point taken in this tick) that a new spawn must keep away from.</summary>
    public struct SpawnThreat
    {
        public float3 Position;
        /// <summary>False = same side as the spawning player (only <see cref="SpawnRules.OccupiedRadius"/> applies).</summary>
        public bool Enemy;
    }

    /// <summary>Server: measures a spawn point against the level and the choppers (fills a <see cref="SpawnCandidate"/>).</summary>
    public static class SpawnProbe
    {
        /// <summary>Ray length when the map has no arena bounds, m.</summary>
        private const float DefaultReach = 1000f;
        private const float BoundsMargin = 50f;

        /// <summary>Walls, ground, sky, bounds. <paramref name="hasWorld"/> false: no physics yet, nothing blocks.</summary>
        public static SpawnCandidate Level(in SpawnPoint point, bool hasWorld, in CollisionWorld world, bool hasBounds,
            in ArenaBounds bounds, in SpawnRules rules)
        {
            var candidate = SpawnCandidate.Clear(point.Index);
            var position = point.Position;
            candidate.InBounds = !hasBounds || bounds.Contains(position);
            if (!hasWorld) return candidate;

            candidate.WallDistance = WallDistance(world, position, math.max(rules.PreferredClearance, rules.WallClearance));
            var bottom = hasBounds ? bounds.Center.y - bounds.HalfExtents.y - BoundsMargin : position.y - DefaultReach;
            var top = hasBounds ? bounds.Center.y + bounds.HalfExtents.y + BoundsMargin : position.y + DefaultReach;
            candidate.GroundHeight = StaticHit(world, position, new float3(position.x, math.min(bottom, position.y - 1f), position.z));
            candidate.OpenSky = float.IsPositiveInfinity(
                StaticHit(world, position, new float3(position.x, math.max(top, position.y + 1f), position.z)));
            return candidate;
        }

        /// <summary>Adds the choppers: nearest enemy, nearest chopper, enemy line of sight.</summary>
        public static void Threats(ref SpawnCandidate candidate, float3 position, NativeArray<SpawnThreat> threats, bool hasWorld,
            in CollisionWorld world, in SpawnRules rules)
        {
            candidate.EnemyDistance = float.PositiveInfinity;
            candidate.ChopperDistance = float.PositiveInfinity;
            candidate.InEnemySight = false;
            foreach (var threat in threats)
            {
                var distance = math.distance(threat.Position, position);
                candidate.ChopperDistance = math.min(candidate.ChopperDistance, distance);
                if (!threat.Enemy) continue;
                candidate.EnemyDistance = math.min(candidate.EnemyDistance, distance);
                if (candidate.InEnemySight || distance > rules.SightRange) continue;
                candidate.InEnemySight = !hasWorld || BotSensing.InSight(world, threat.Position, position);
            }
        }

        /// <summary>Distance to the nearest static collider, up to <paramref name="reach"/>; infinity when none is that near.</summary>
        private static float WallDistance(in CollisionWorld world, float3 position, float reach)
        {
            var hits = new NativeList<DistanceHit>(8, Allocator.Temp);
            var nearest = float.PositiveInfinity;
            var input = new PointDistanceInput { Position = position, MaxDistance = reach, Filter = CollisionFilter.Default };
            if (world.CalculateDistance(input, ref hits))
            {
                foreach (var hit in hits)
                    if (hit.RigidBodyIndex >= world.NumDynamicBodies) nearest = math.min(nearest, math.max(hit.Distance, 0f));
            }
            hits.Dispose();
            return nearest;
        }

        /// <summary>Distance along the ray to the first static collider; infinity when the ray is free (choppers do not count).</summary>
        private static float StaticHit(in CollisionWorld world, float3 from, float3 to)
        {
            var hits = new NativeList<RaycastHit>(8, Allocator.Temp);
            var nearest = float.PositiveInfinity;
            if (world.CastRay(new RaycastInput { Start = from, End = to, Filter = CollisionFilter.Default }, ref hits))
            {
                var length = math.distance(from, to);
                foreach (var hit in hits)
                    if (hit.RigidBodyIndex >= world.NumDynamicBodies) nearest = math.min(nearest, hit.Fraction * length);
            }
            hits.Dispose();
            return nearest;
        }
    }
}
