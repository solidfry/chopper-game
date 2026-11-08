using Unity.Collections;
using Unity.Mathematics;

namespace Chopper.Simulation.Bots
{
    /// <summary>Which cells an agent may use.</summary>
    public enum NavDomain : byte
    {
        /// <summary>Any free cell (choppers, drones).</summary>
        Air,
        /// <summary>Floor cells only (free with solid below); steps of one cell up or down (ground units).</summary>
        Ground,
    }

    /// <summary>One path query. Clearances are in cells.</summary>
    public struct NavQuery
    {
        public NavDomain Domain;
        /// <summary>Cells closer to a wall than this are not used.</summary>
        public byte MinClearance;
        /// <summary>Cells closer than this cost more (keeps paths in open air).</summary>
        public byte PreferredClearance;
        /// <summary>Extra cost at a wall (MinClearance), fading to 0 at <see cref="PreferredClearance"/>. 1 = twice the length.</summary>
        public float ClearancePenalty;
        /// <summary>Extra cost per metre of climb (0 = climbing is free).</summary>
        public float ClimbCost;
        /// <summary>Height over street level (<see cref="NavGridBlob.Floor"/>, m) that costs nothing extra.</summary>
        public float CruiseHeight;
        /// <summary>Extra cost per 100 m above <see cref="CruiseHeight"/> (1 = twice the length): keeps air paths low.</summary>
        public float HeightCost;
        /// <summary>Most cells the search may open. When it runs out, the path goes to the closest cell found.</summary>
        public int MaxNodes;
    }

    public enum NavPathStatus : byte
    {
        /// <summary>Start or goal has no usable cell nearby.</summary>
        None,
        Found,
        /// <summary>The goal was not reached (search budget, or no way there): the path ends at the closest point.</summary>
        Partial,
    }

    /// <summary>
    /// A* over a <see cref="NavGridBlob"/> (26 neighbours in the air, 8 plus steps on the ground), then string pulling:
    /// waypoints that the grid line test can skip are dropped. The straight line is tried first, so open sky costs
    /// nothing. Pure and Burst friendly (temporary allocations only).
    /// </summary>
    public static class NavPathfinder
    {
        /// <summary>How far (cells) to look for a usable cell when the start or goal cell is not.</summary>
        private const int SnapRadius = 4;
        private const float HeuristicWeight = 1.2f;

        /// <summary>
        /// Writes the path to <paramref name="path"/> (cleared first): waypoints after the start, the last is the goal or
        /// the closest point to it.
        /// </summary>
        public static NavPathStatus FindPath(ref NavGridBlob grid, float3 start, float3 goal, in NavQuery query, ref NativeList<float3> path)
        {
            path.Clear();
            var minClearance = (byte)math.max((int)query.MinClearance, 1);
            if (!Snap(ref grid, grid.CellAt(start), query, minClearance, out var startCell)) return NavPathStatus.None;
            var goalReachable = Snap(ref grid, grid.CellAt(goal), query, minClearance, out var goalCell);
            if (!goalReachable) goalCell = grid.CellAt(goal);

            if (goalReachable && query.Domain == NavDomain.Air && grid.LineClear(start, goal, minClearance))
            {
                path.Add(goal);
                return NavPathStatus.Found;
            }

            var startIndex = grid.Index(startCell);
            var goalIndex = goalReachable ? grid.Index(goalCell) : -1;
            var goalCentre = grid.Centre(goalCell);

            var open = new NativeList<HeapNode>(256, Allocator.Temp);
            var cost = new NativeHashMap<int, float>(1024, Allocator.Temp);
            var parent = new NativeHashMap<int, int>(1024, Allocator.Temp);
            var closed = new NativeHashSet<int>(1024, Allocator.Temp);

            cost[startIndex] = 0f;
            parent[startIndex] = -1;
            Push(ref open, new HeapNode { Index = startIndex, Score = Heuristic(ref grid, startCell, goalCentre) });

            var best = startIndex;
            var bestDistance = math.distance(grid.Centre(startCell), goalCentre);
            var reached = false;
            var opened = 0;

            while (open.Length > 0 && opened < query.MaxNodes)
            {
                var current = Pop(ref open);
                if (!closed.Add(current.Index)) continue;
                opened++;
                if (current.Index == goalIndex)
                {
                    reached = true;
                    best = current.Index;
                    break;
                }

                var cell = grid.CellOf(current.Index);
                var centre = grid.Centre(cell);
                var distance = math.distance(centre, goalCentre);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = current.Index;
                }

                var currentCost = cost[current.Index];
                for (var dy = -1; dy <= 1; dy++)
                for (var dz = -1; dz <= 1; dz++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0 && dz == 0) continue;
                    var next = cell + new int3(dx, dy, dz);
                    if (!Usable(ref grid, next, query, minClearance)) continue;
                    if (query.Domain == NavDomain.Ground && dx == 0 && dz == 0) continue; // no straight up or down
                    var nextIndex = grid.Index(next);
                    if (closed.Contains(nextIndex)) continue;

                    var step = math.length(new float3(dx, dy, dz)) * grid.CellSize;
                    var clearance = grid.Clearance[nextIndex];
                    var crowd = query.PreferredClearance > minClearance
                        ? math.saturate((float)(query.PreferredClearance - clearance) / (query.PreferredClearance - minClearance))
                        : 0f;
                    var climb = dy > 0 ? grid.CellSize * query.ClimbCost : 0f;
                    var nextCentre = grid.Centre(next);
                    var high = math.max(0f, nextCentre.y - grid.FloorAt(nextCentre) - query.CruiseHeight) * 0.01f;
                    var nextCost = currentCost + step * (1f + query.ClearancePenalty * crowd + query.HeightCost * high) + climb;
                    if (cost.TryGetValue(nextIndex, out var known) && known <= nextCost) continue;

                    cost[nextIndex] = nextCost;
                    parent[nextIndex] = current.Index;
                    Push(ref open, new HeapNode { Index = nextIndex, Score = nextCost + Heuristic(ref grid, next, goalCentre) });
                }
            }

            // Walk back from the end cell, then string-pull forwards from the start.
            var cells = new NativeList<float3>(64, Allocator.Temp);
            for (var index = best; index != -1; index = parent[index])
                cells.Add(grid.Centre(grid.CellOf(index)));
            var end = reached ? goal : cells[0];
            cells[0] = end;
            Reverse(ref cells);

            var from = start;
            var i = 0;
            while (i < cells.Length - 1)
            {
                // The farthest point that the straight line still reaches.
                var far = i + 1;
                for (var j = cells.Length - 1; j > i + 1; j--)
                {
                    if (!LineClear(ref grid, from, cells[j], query.Domain, minClearance)) continue;
                    far = j;
                    break;
                }
                path.Add(cells[far]);
                from = cells[far];
                i = far;
            }
            if (path.Length == 0) path.Add(end);

            open.Dispose();
            cost.Dispose();
            parent.Dispose();
            closed.Dispose();
            cells.Dispose();
            return reached ? NavPathStatus.Found : NavPathStatus.Partial;
        }

        /// <summary>Total length of a path from <paramref name="from"/>, starting at waypoint <paramref name="next"/>.</summary>
        public static float Remaining(float3 from, in NativeArray<float3> path, int next)
        {
            var length = 0f;
            for (var i = next; i < path.Length; i++)
            {
                length += math.distance(from, path[i]);
                from = path[i];
            }
            return length;
        }

        /// <summary>
        /// Air: every point keeps the clearance. Ground: also a floor cell within one cell of every point (no driving
        /// over gaps or up walls).
        /// </summary>
        public static bool LineClear(ref NavGridBlob grid, float3 from, float3 to, NavDomain domain, byte minClearance)
        {
            if (domain == NavDomain.Air) return grid.LineClear(from, to, minClearance);

            var offset = to - from;
            var steps = (int)math.ceil(math.length(offset) / (grid.CellSize * 0.5f));
            for (var i = 0; i <= steps; i++)
            {
                var cell = grid.CellAt(from + offset * (steps == 0 ? 0f : (float)i / steps));
                var floor = false;
                for (var dy = -1; dy <= 1 && !floor; dy++)
                {
                    var candidate = cell + new int3(0, dy, 0);
                    floor = grid.IsFloor(candidate) && grid.ClearanceAt(candidate) >= minClearance;
                }
                if (!floor) return false;
            }
            return true;
        }

        private static bool Usable(ref NavGridBlob grid, int3 cell, in NavQuery query, byte minClearance)
        {
            if (grid.ClearanceAt(cell) < minClearance) return false;
            return query.Domain == NavDomain.Air || grid.IsFloor(cell);
        }

        /// <summary>The nearest usable cell to <paramref name="cell"/> within <see cref="SnapRadius"/> (the cell itself first).</summary>
        private static bool Snap(ref NavGridBlob grid, int3 cell, in NavQuery query, byte minClearance, out int3 snapped)
        {
            snapped = math.clamp(cell, 0, grid.Size - 1);
            if (Usable(ref grid, snapped, query, minClearance)) return true;

            var centre = snapped;
            var bestDistance = int.MaxValue;
            var found = false;
            for (var dy = -SnapRadius; dy <= SnapRadius; dy++)
            for (var dz = -SnapRadius; dz <= SnapRadius; dz++)
            for (var dx = -SnapRadius; dx <= SnapRadius; dx++)
            {
                var candidate = centre + new int3(dx, dy, dz);
                var distance = dx * dx + dy * dy + dz * dz;
                if (distance >= bestDistance || !Usable(ref grid, candidate, query, minClearance)) continue;
                bestDistance = distance;
                snapped = candidate;
                found = true;
            }
            return found;
        }

        private static float Heuristic(ref NavGridBlob grid, int3 cell, float3 goal) =>
            math.distance(grid.Centre(cell), goal) * HeuristicWeight;

        private struct HeapNode
        {
            public int Index;
            public float Score;
        }

        private static void Push(ref NativeList<HeapNode> heap, HeapNode node)
        {
            heap.Add(node);
            var i = heap.Length - 1;
            while (i > 0)
            {
                var up = (i - 1) / 2;
                if (heap[up].Score <= heap[i].Score) break;
                var swap = heap[up];
                heap[up] = heap[i];
                heap[i] = swap;
                i = up;
            }
        }

        private static HeapNode Pop(ref NativeList<HeapNode> heap)
        {
            var top = heap[0];
            var last = heap.Length - 1;
            heap[0] = heap[last];
            heap.RemoveAt(last);
            var i = 0;
            while (true)
            {
                var left = i * 2 + 1;
                if (left >= heap.Length) break;
                var right = left + 1;
                var smallest = right < heap.Length && heap[right].Score < heap[left].Score ? right : left;
                if (heap[i].Score <= heap[smallest].Score) break;
                var swap = heap[i];
                heap[i] = heap[smallest];
                heap[smallest] = swap;
                i = smallest;
            }
            return top;
        }

        private static void Reverse(ref NativeList<float3> list)
        {
            for (int a = 0, b = list.Length - 1; a < b; a++, b--)
            {
                var swap = list[a];
                list[a] = list[b];
                list[b] = swap;
            }
        }
    }
}
