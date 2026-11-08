using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;

namespace Chopper.Simulation.Bots
{
    /// <summary>
    /// The bot's eyes for flying: short sphere casts along where it is going, against the level colliders and other
    /// choppers (not itself). Results feed <see cref="BotPilot"/> as an <see cref="ObstacleSense"/>.
    /// </summary>
    public static class BotSensing
    {
        /// <summary>Below this speed (m/s) the look-ahead follows the wanted direction, not the velocity.</summary>
        private const float SlowSpeed = 3f;
        /// <summary>The second probe looks this far below the flight path (radians), to catch the ground and roofs ahead.</summary>
        private const float DownProbeAngle = 0.35f;

        /// <summary>
        /// Looks ahead along the velocity (stopping distance plus <see cref="BotHandling.ProbeSeconds"/> of travel), a little
        /// below it, and along the wanted direction. Returns the hit with the least time to impact.
        /// </summary>
        public static ObstacleSense Probe(in CollisionWorld world, Entity self, float3 position, float3 velocity,
            float3 wantedDirection, float maxAcceleration, in BotHandling handling)
        {
            var speed = math.length(velocity);
            var stopping = speed * speed / (2f * math.max(maxAcceleration, 1f));
            var length = math.max(handling.MinProbeDistance, speed * handling.ProbeSeconds + stopping);
            var travel = speed > SlowSpeed ? velocity / speed : math.normalizesafe(wantedDirection, math.forward());

            var best = ObstacleSense.Clear;
            var hits = new NativeList<ColliderCastHit>(8, Allocator.Temp);
            Cast(world, self, position, travel, length, velocity, handling.ProbeRadius, ref hits, ref best);

            // Slightly down: the path a descending or levelling chopper will sweep.
            var side = math.cross(math.up(), travel);
            if (math.lengthsq(side) > 1e-4f)
            {
                var down = math.mul(quaternion.AxisAngle(math.normalize(side), DownProbeAngle), travel);
                Cast(world, self, position, down, length * 0.7f, velocity, handling.ProbeRadius, ref hits, ref best);
            }

            if (math.lengthsq(wantedDirection) > 1e-4f && math.dot(math.normalize(wantedDirection), travel) < 0.95f)
                Cast(world, self, position, math.normalize(wantedDirection), length * 0.6f, velocity, handling.ProbeRadius, ref hits, ref best);

            hits.Dispose();
            return best;
        }

        /// <summary>
        /// Height of the top surface (ground or roof) below <paramref name="x"/>, <paramref name="z"/>, searched from
        /// <paramref name="top"/> down to <paramref name="bottom"/>. False when nothing is there.
        /// </summary>
        public static bool SurfaceHeight(in CollisionWorld world, float x, float z, float top, float bottom, out float height)
        {
            var input = new RaycastInput
            {
                Start = new float3(x, top, z),
                End = new float3(x, bottom, z),
                Filter = CollisionFilter.Default,
            };
            if (world.CastRay(input, out var hit))
            {
                height = hit.Position.y;
                return true;
            }
            height = bottom;
            return false;
        }

        /// <summary>
        /// Sphere cast from <paramref name="from"/> to <paramref name="to"/>. Returns the first hit point that is not
        /// <paramref name="self"/>, or false when the way is clear.
        /// </summary>
        public static bool Blocked(in CollisionWorld world, Entity self, float3 from, float3 to, float radius, out float3 hitPosition)
        {
            hitPosition = default;
            var offset = to - from;
            var distance = math.length(offset);
            if (distance < 1e-3f) return false;

            var hits = new NativeList<ColliderCastHit>(8, Allocator.Temp);
            var found = false;
            var closest = float.MaxValue;
            if (world.SphereCastAll(from, radius, offset / distance, distance, ref hits, CollisionFilter.Default, QueryInteraction.IgnoreTriggers))
            {
                foreach (var hit in hits)
                {
                    if (hit.Entity == self || hit.Fraction >= closest) continue;
                    closest = hit.Fraction;
                    hitPosition = hit.Position;
                    found = true;
                }
            }
            hits.Dispose();
            return found;
        }

        private static void Cast(in CollisionWorld world, Entity self, float3 origin, float3 direction, float length,
            float3 velocity, float radius, ref NativeList<ColliderCastHit> hits, ref ObstacleSense best)
        {
            hits.Clear();
            if (!world.SphereCastAll(origin, radius, direction, length, ref hits, CollisionFilter.Default, QueryInteraction.IgnoreTriggers))
                return;

            foreach (var hit in hits)
            {
                if (hit.Entity == self) continue;
                var distance = hit.Fraction * length;
                // Closing speed along the probe; a slow approach still counts a little.
                var closing = math.max(math.dot(velocity, direction), 1f);
                var timeToImpact = distance / closing;
                if (timeToImpact >= best.TimeToImpact) continue;
                best = new ObstacleSense
                {
                    Distance = distance,
                    Normal = math.normalizesafe(hit.SurfaceNormal, -direction),
                    ProbeLength = length,
                    TimeToImpact = timeToImpact,
                };
            }
        }
    }
}
