using Chopper.Simulation.Bots;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Chopper.Tests
{
    /// <summary><see cref="NavPathfinder"/> on small hand-made grids (10 m cells).</summary>
    public sealed class NavPathfinderTests
    {
        private const float Cell = 10f;

        /// <summary>
        /// A 30 x 12 x 30 grid: solid ground (layer 0) and a wall at x = 15 up to layer <paramref name="wallHeight"/>,
        /// with an open gap at z 20..22 when <paramref name="gap"/>. Clearance by a plain breadth-first search.
        /// </summary>
        private static BlobAssetReference<NavGridBlob> Grid(int wallHeight, bool gap, int wallFromZ = 0, int wallToZ = 29)
        {
            var size = new int3(30, 12, 30);
            var solid = new bool[size.x * size.y * size.z];
            int Index(int x, int y, int z) => x + size.x * (z + size.z * y);
            for (var z = 0; z < size.z; z++)
            for (var x = 0; x < size.x; x++)
            {
                solid[Index(x, 0, z)] = true;
                if (x != 15 || z < wallFromZ || z > wallToZ || (gap && z >= 20 && z <= 22)) continue;
                for (var y = 1; y <= wallHeight; y++) solid[Index(x, y, z)] = true;
            }

            var clearance = new byte[solid.Length];
            var queue = new System.Collections.Generic.Queue<int>();
            for (var i = 0; i < solid.Length; i++)
            {
                clearance[i] = solid[i] ? (byte)0 : (byte)255;
                if (solid[i]) queue.Enqueue(i);
            }
            while (queue.Count > 0)
            {
                var i = queue.Dequeue();
                var y = i / (size.x * size.z);
                var z = (i - y * size.x * size.z) / size.x;
                var x = i % size.x;
                for (var dy = -1; dy <= 1; dy++)
                for (var dz = -1; dz <= 1; dz++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy, nz = z + dz;
                    if (nx < 0 || ny < 0 || nz < 0 || nx >= size.x || ny >= size.y || nz >= size.z) continue;
                    var n = Index(nx, ny, nz);
                    if (clearance[n] != 255) continue;
                    clearance[n] = (byte)(clearance[i] + 1);
                    queue.Enqueue(n);
                }
            }

            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<NavGridBlob>();
            root.Origin = float3.zero;
            root.CellSize = Cell;
            root.Size = size;
            var array = builder.Allocate(ref root.Clearance, clearance.Length);
            for (var i = 0; i < clearance.Length; i++) array[i] = clearance[i];
            var floor = builder.Allocate(ref root.Floor, size.x * size.z);
            for (var i = 0; i < floor.Length; i++) floor[i] = Cell; // top of the ground layer
            var blob = builder.CreateBlobAssetReference<NavGridBlob>(Allocator.Persistent);
            builder.Dispose();
            return blob;
        }

        private static NavQuery Query(NavDomain domain) => new()
        {
            Domain = domain, MinClearance = 1, PreferredClearance = 3, ClearancePenalty = 1f, ClimbCost = 0.2f, MaxNodes = 50000,
            CruiseHeight = 30f, HeightCost = 1.5f,
        };

        private static void AssertPathClear(ref NavGridBlob grid, float3 start, NativeList<float3> path, NavDomain domain)
        {
            var from = start;
            foreach (var point in path)
            {
                Assert.IsTrue(NavPathfinder.LineClear(ref grid, from, point, domain, 1), $"blocked from {from} to {point}");
                from = point;
            }
        }

        [Test]
        public void OpenSky_IsOneStraightLine()
        {
            var blob = Grid(wallHeight: 3, gap: false);
            var path = new NativeList<float3>(Allocator.Temp);
            var status = NavPathfinder.FindPath(ref blob.Value, new float3(50, 90, 50), new float3(250, 90, 250), Query(NavDomain.Air), ref path);
            Assert.AreEqual(NavPathStatus.Found, status);
            Assert.AreEqual(1, path.Length);
            path.Dispose();
            blob.Dispose();
        }

        [Test]
        public void Air_GoesOverAWall()
        {
            var blob = Grid(wallHeight: 8, gap: false);
            var path = new NativeList<float3>(Allocator.Temp);
            var start = new float3(50, 30, 150);
            var goal = new float3(250, 30, 150);
            var status = NavPathfinder.FindPath(ref blob.Value, start, goal, Query(NavDomain.Air), ref path);
            Assert.AreEqual(NavPathStatus.Found, status);
            Assert.Greater(path.Length, 1);
            Assert.AreEqual(goal, path[path.Length - 1]);
            AssertPathClear(ref blob.Value, start, path, NavDomain.Air);
            path.Dispose();
            blob.Dispose();
        }

        [Test]
        public void Air_GoesRoundATallTower_RatherThanOverIt()
        {
            // Tower: x 150..160, z 100..200, 110 m high; open air beside it.
            var blob = Grid(wallHeight: 10, gap: false, wallFromZ: 10, wallToZ: 19);
            var path = new NativeList<float3>(Allocator.Temp);
            var start = new float3(50, 25, 150);
            var goal = new float3(250, 25, 150);
            var status = NavPathfinder.FindPath(ref blob.Value, start, goal, Query(NavDomain.Air), ref path);
            Assert.AreEqual(NavPathStatus.Found, status);
            AssertPathClear(ref blob.Value, start, path, NavDomain.Air);
            foreach (var point in path) Assert.Less(point.y, 80f, $"went over the tower at {point}");
            path.Dispose();
            blob.Dispose();
        }

        [Test]
        public void Ground_UsesTheGap()
        {
            var blob = Grid(wallHeight: 11, gap: true);
            var path = new NativeList<float3>(Allocator.Temp);
            var start = new float3(50, 15, 50);
            var goal = new float3(250, 15, 50);
            var status = NavPathfinder.FindPath(ref blob.Value, start, goal, Query(NavDomain.Ground), ref path);
            Assert.AreEqual(NavPathStatus.Found, status);
            AssertPathClear(ref blob.Value, start, path, NavDomain.Ground);
            var throughGap = false;
            var from = start;
            foreach (var point in path)
            {
                // The segment that crosses x = 155 must be inside the gap (z 200..230).
                if ((from.x - 155f) * (point.x - 155f) <= 0f)
                {
                    var t = (155f - from.x) / (point.x - from.x);
                    var z = math.lerp(from.z, point.z, t);
                    throughGap |= z > 195f && z < 235f;
                }
                from = point;
            }
            Assert.IsTrue(throughGap, "the ground path did not go through the gap");
            path.Dispose();
            blob.Dispose();
        }

        [Test]
        public void Ground_NoWay_IsPartial()
        {
            var blob = Grid(wallHeight: 11, gap: false);
            var path = new NativeList<float3>(Allocator.Temp);
            var status = NavPathfinder.FindPath(ref blob.Value, new float3(50, 15, 50), new float3(250, 15, 50), Query(NavDomain.Ground), ref path);
            Assert.AreEqual(NavPathStatus.Partial, status);
            Assert.Less(path[path.Length - 1].x, 150f);
            path.Dispose();
            blob.Dispose();
        }
    }
}
