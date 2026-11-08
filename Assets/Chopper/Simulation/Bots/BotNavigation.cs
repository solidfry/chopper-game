using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Chopper.Simulation.Bots
{
    /// <summary>A bot's current path (waypoints after its position when planned; the last is the goal).</summary>
    [InternalBufferCapacity(16)]
    public struct BotWaypoint : IBufferElementData
    {
        public float3 Position;
    }

    /// <summary>Path following state. See <see cref="BotNavigation"/>.</summary>
    public struct BotNavigator : IComponentData
    {
        public float3 Goal;
        public NavPathStatus Status;
        public int Next;
        public double ReplanAt;
        public double EarliestReplan;
        public bool Planned;
    }

    /// <summary>
    /// The navigator: plans a path on the <see cref="NavGrid"/> to the brain's goal and gives the pilot the next waypoint,
    /// with the distance still to go (so the pilot brakes for the end of the path, not for every corner). Plans again
    /// when the goal moves, after <see cref="BotHandling.ReplanSeconds"/>, or when the next waypoint is out of sight.
    /// Looks ahead: when the waypoint after the next is in sight, it goes straight there.
    /// </summary>
    public static class BotNavigation
    {
        /// <summary>The fastest repeat when plans keep failing (s).</summary>
        private const double MinReplanSeconds = 0.5;

        public static NavQuery Query(in BotHandling handling, float cellSize, NavDomain domain) => new()
        {
            Domain = domain,
            MinClearance = Cells(handling.PathMinClearance, cellSize, 1),
            PreferredClearance = Cells(handling.PathPreferredClearance, cellSize, 1),
            ClearancePenalty = handling.PathClearancePenalty,
            ClimbCost = handling.PathClimbCost,
            CruiseHeight = handling.PathCruiseHeight,
            HeightCost = handling.PathHeightCost,
            MaxNodes = math.max(handling.PathMaxNodes, 100),
        };

        /// <summary>Sets <see cref="PilotOrder.Destination"/> and <see cref="PilotOrder.RemainingDistance"/> on the way to <paramref name="goal"/>.</summary>
        public static void Follow(ref NavGridBlob grid, ref BotNavigator navigator, DynamicBuffer<BotWaypoint> waypoints,
            float3 position, float3 goal, double now, in BotHandling handling, NavDomain domain, ref PilotOrder order)
        {
            var query = Query(handling, grid.CellSize, domain);
            var replan = !navigator.Planned || now >= navigator.ReplanAt ||
                         math.distance(goal, navigator.Goal) > grid.CellSize * 2f;
            if (!replan && navigator.Next < waypoints.Length &&
                !NavPathfinder.LineClear(ref grid, position, waypoints[navigator.Next].Position, domain, query.MinClearance))
                replan = true;

            if (replan && now >= navigator.EarliestReplan)
            {
                var path = new NativeList<float3>(16, Allocator.Temp);
                navigator.Status = NavPathfinder.FindPath(ref grid, position, goal, query, ref path);
                waypoints.Clear();
                foreach (var point in path) waypoints.Add(new BotWaypoint { Position = point });
                path.Dispose();
                navigator.Goal = goal;
                navigator.Next = 0;
                navigator.Planned = true;
                navigator.ReplanAt = now + handling.ReplanSeconds;
                navigator.EarliestReplan = now + MinReplanSeconds;
            }

            if (navigator.Status == NavPathStatus.None || waypoints.Length == 0)
            {
                order.Destination = goal;
                order.RemainingDistance = 0f;
                return;
            }

            var reach = grid.CellSize * 1.5f;
            while (navigator.Next < waypoints.Length - 1 &&
                   (math.distance(position, waypoints[navigator.Next].Position) < reach ||
                    NavPathfinder.LineClear(ref grid, position, waypoints[navigator.Next + 1].Position, domain, query.MinClearance)))
                navigator.Next++;

            order.Destination = waypoints[navigator.Next].Position;
            order.RemainingDistance = NavPathfinder.Remaining(position, waypoints.AsNativeArray().Reinterpret<float3>(), navigator.Next);
        }

        private static byte Cells(float metres, float cellSize, int minimum) =>
            (byte)math.clamp((int)math.ceil(metres / math.max(cellSize, 0.1f)), minimum, 254);
    }
}
