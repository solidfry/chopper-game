using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Random = Unity.Mathematics.Random;

namespace Chopper.Simulation.Bots
{
    /// <summary>
    /// Stand-in brain until the real one (plan section 7a): flies to random points over the map at a random height over
    /// the ground or roofs. When a building is in the straight line, the waypoint goes up over it (the path finder will
    /// replace this).
    /// </summary>
    public struct BotWander : IComponentData
    {
        public Random Random;
        public bool HasGoal;
        public float3 Goal;
        public double GoalTime;
        /// <summary>The goal, raised over whatever blocks the straight line.</summary>
        public float3 Waypoint;
        public double NextLineCheck;

        /// <summary>Seconds between line-of-flight checks to the goal.</summary>
        private const double LineCheckSeconds = 0.5;

        public static BotWander Create(uint seed) => new() { Random = Random.CreateFromIndex(seed) };

        public void Think(in CollisionWorld world, Entity self, float3 position, double now, in PilotSense sense,
            in BotHandling handling, ref PilotOrder order)
        {
            var reached = HasGoal && math.distance(position.xz, Goal.xz) < handling.ArriveRadius * 1.5f &&
                          math.abs(position.y - Goal.y) < 60f;
            if (!HasGoal || reached || now - GoalTime > handling.WanderSeconds)
                PickGoal(world, now, sense, handling, position);

            if (now >= NextLineCheck)
            {
                NextLineCheck = now + LineCheckSeconds;
                Waypoint = Goal;
                if (BotSensing.Blocked(world, self, position, Goal, handling.ProbeRadius, out var hit))
                    Waypoint.y = math.max(Goal.y, hit.y + handling.MinClearance * 2f);
            }

            order.Destination = Waypoint;
            order.Speed = handling.CruiseSpeed;
            order.ArriveRadius = handling.ArriveRadius;
            order.AllowBoost = true;
            order.Aim = false;
            order.Fire = false;
            order.FireSecondary = false;
        }

        private void PickGoal(in CollisionWorld world, double now, in PilotSense sense, in BotHandling handling, float3 position)
        {
            var centre = sense.HasBounds ? sense.BoundsCentre : float3.zero;
            var half = sense.HasBounds ? sense.BoundsHalfExtents : new float3(1000f, 450f, 1000f);
            var inner = math.max(half - handling.BoundsMargin * 1.5f, 10f);
            var top = centre.y + half.y;
            var bottom = centre.y - half.y;

            // A few tries for one far enough away to be a real leg.
            var goal = position;
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var xz = centre.xz + Random.NextFloat2(-inner.xz, inner.xz);
                BotSensing.SurfaceHeight(world, xz.x, xz.y, top, bottom, out var surface);
                var height = surface + Random.NextFloat(handling.WanderClearance.x, handling.WanderClearance.y);
                goal = new float3(xz.x, math.min(height, top - handling.BoundsMargin), xz.y);
                if (math.distance(goal.xz, position.xz) > handling.BoostDistance) break;
            }

            Goal = goal;
            Waypoint = goal;
            HasGoal = true;
            GoalTime = now;
            NextLineCheck = 0d;
        }
    }
}
