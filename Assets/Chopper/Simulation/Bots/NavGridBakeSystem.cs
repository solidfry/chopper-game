using Chopper.Simulation.Combat;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;

namespace Chopper.Simulation.Bots
{
    /// <summary>
    /// Bakes the <see cref="NavGrid"/> from the level's static colliders once the map is loaded and a bot exists in this
    /// world, or the world is a server whose map allows bots (<see cref="BotFillRules"/>). No bots, no cost. The work runs in jobs over a copy of the collision world, so the game does not stall:
    /// <list type="number">
    /// <item>Column tops: a sphere cast down each column finds the highest static surface (cells above it are free);
    /// street level = the lowest top within two columns.</item>
    /// <item>Solid cells: a static collider within half a cell diagonal of the cell centre (only below the column top).</item>
    /// <item>Reachable air: a flood fill from the top layer; free cells it cannot reach (inside buildings, under the
    /// terrain: the level meshes are hollow) become solid.</item>
    /// <item>Clearance: distance in cells to the nearest solid cell (26 neighbours), up to 255.</item>
    /// </list>
    /// When the map unloads (the arena bounds go), the grid goes too.
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct NavGridBakeSystem : ISystem
    {
        /// <summary>The static colliders must stay the same this long (s) before the bake starts (subscene streaming).</summary>
        private const double SettleSeconds = 1.0;
        /// <summary>Larger grids get bigger cells.</summary>
        private const int MaxCells = 8_000_000;

        private const byte Solid = 0;
        private const byte Free = 1;
        private const byte Reached = 2;

        private Entity _gridEntity;
        private BlobAssetReference<NavGridBlob> _blob;

        private bool _baking;
        private JobHandle _bake;
        private CollisionWorld _world;
        private NativeArray<float> _tops;
        private NativeArray<float> _floors;
        private NativeArray<byte> _cells;
        private NativeArray<int> _queue;
        private float3 _origin;
        private float _cellSize;
        private int3 _size;
        private double _bakeStart;

        private int _staticBodies;
        private double _settledSince;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BotHandling>();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            DisposeBake();
            if (_blob.IsCreated) _blob.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var now = SystemAPI.Time.ElapsedTime;
            var hasBounds = SystemAPI.TryGetSingleton<ArenaBounds>(out var bounds);

            if (_blob.IsCreated)
            {
                if (hasBounds) return;
                // Map unloaded.
                if (state.EntityManager.Exists(_gridEntity)) state.EntityManager.DestroyEntity(_gridEntity);
                _blob.Dispose();
                _blob = default;
                return;
            }

            if (_baking)
            {
                if (!_bake.IsCompleted) return;
                _bake.Complete();
                Finish(ref state, now);
                return;
            }

            if (!hasBounds || !SystemAPI.HasSingleton<PhysicsWorldSingleton>()) return;
            // A bot exists, or this is a server that may add bots (the grid is then ready before the first one).
            var mayFill = state.WorldUnmanaged.IsServer() && SystemAPI.TryGetSingleton<BotFillRules>(out var fill) && fill.MaxBots > 0;
            if (!mayFill && SystemAPI.QueryBuilder().WithAll<BotPilotState>().Build().IsEmpty) return;

            var physics = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
            var staticBodies = physics.CollisionWorld.NumStaticBodies;
            if (staticBodies != _staticBodies || staticBodies <= 1)
            {
                _staticBodies = staticBodies;
                _settledSince = now;
                return;
            }
            if (now - _settledSince < SettleSeconds) return;

            state.CompleteDependency();
            Start(physics.CollisionWorld, bounds, SystemAPI.GetSingleton<BotHandling>().NavCellSize, now);
        }

        private void Start(in CollisionWorld world, in ArenaBounds bounds, float cellSize, double now)
        {
            cellSize = math.max(cellSize, 2f);
            var extent = bounds.HalfExtents * 2f;
            var size = (int3)math.ceil(extent / cellSize);
            while ((long)size.x * size.y * size.z > MaxCells)
            {
                cellSize *= 1.25f;
                size = (int3)math.ceil(extent / cellSize);
            }

            _origin = bounds.Center - bounds.HalfExtents;
            _cellSize = cellSize;
            _size = math.max(size, 1);
            var count = _size.x * _size.y * _size.z;

            _world = world.Clone();
            _tops = new NativeArray<float>(_size.x * _size.z, Allocator.Persistent);
            _floors = new NativeArray<float>(_size.x * _size.z, Allocator.Persistent);
            _cells = new NativeArray<byte>(count, Allocator.Persistent);
            _queue = new NativeArray<int>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            _bakeStart = now;
            _baking = true;

            var tops = new ColumnTopJob
            {
                World = _world, Origin = _origin, CellSize = _cellSize, Size = _size, Tops = _tops,
            }.Schedule(_tops.Length, 64);
            var floors = new FloorJob { Size = _size, Tops = _tops, Floors = _floors }.Schedule(_floors.Length, 256, tops);
            var solid = new SolidJob
            {
                World = _world, Origin = _origin, CellSize = _cellSize, Size = _size, Tops = _tops, Cells = _cells,
            }.Schedule(count, 256, tops);
            var flood = new FloodJob { Size = _size, Cells = _cells, Queue = _queue }.Schedule(solid);
            _bake = JobHandle.CombineDependencies(new ClearanceJob { Size = _size, Cells = _cells, Queue = _queue }.Schedule(flood), floors);
        }

        private void Finish(ref SystemState state, double now)
        {
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<NavGridBlob>();
            root.Origin = _origin;
            root.CellSize = _cellSize;
            root.Size = _size;
            var clearance = builder.Allocate(ref root.Clearance, _cells.Length);
            var floor = builder.Allocate(ref root.Floor, _floors.Length);
            for (var i = 0; i < _floors.Length; i++) floor[i] = _floors[i];
            var free = 0;
            for (var i = 0; i < _cells.Length; i++)
            {
                clearance[i] = _cells[i];
                if (_cells[i] > 0) free++;
            }
            _blob = builder.CreateBlobAssetReference<NavGridBlob>(Allocator.Persistent);
            builder.Dispose();

            _gridEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(_gridEntity, new NavGrid { Blob = _blob });

            var seconds = (float)(now - _bakeStart);
            var percent = 100f * free / math.max(_cells.Length, 1);
            UnityEngine.Debug.Log($"[Nav] Grid {_size.x}x{_size.y}x{_size.z} of {_cellSize} m baked in {seconds} s; {percent} % open air.");
            DisposeBake();
        }

        private void DisposeBake()
        {
            if (!_baking) return;
            _bake.Complete();
            _world.Dispose();
            if (_tops.IsCreated) _tops.Dispose();
            if (_floors.IsCreated) _floors.Dispose();
            if (_cells.IsCreated) _cells.Dispose();
            if (_queue.IsCreated) _queue.Dispose();
            _baking = false;
        }

        private static bool IsStatic(in CollisionWorld world, int rigidBodyIndex) => rigidBodyIndex >= world.NumDynamicBodies;

        /// <summary>Height of the highest static surface in each column (a sphere as wide as the column, cast down).</summary>
        [BurstCompile]
        private struct ColumnTopJob : IJobParallelFor
        {
            [ReadOnly] public CollisionWorld World;
            public float3 Origin;
            public float CellSize;
            public int3 Size;
            public NativeArray<float> Tops;

            public void Execute(int column)
            {
                var x = column % Size.x;
                var z = column / Size.x;
                var top = Origin.y + Size.y * CellSize;
                var bottom = Origin.y;
                var radius = CellSize * 0.75f;
                var start = new float3(Origin.x + (x + 0.5f) * CellSize, top + radius, Origin.z + (z + 0.5f) * CellSize);

                var hits = new NativeList<ColliderCastHit>(8, Allocator.Temp);
                var highest = bottom - CellSize;
                if (World.SphereCastAll(start, radius, new float3(0f, -1f, 0f), top - bottom + radius, ref hits, CollisionFilter.Default, QueryInteraction.IgnoreTriggers))
                {
                    foreach (var hit in hits)
                        if (IsStatic(World, hit.RigidBodyIndex)) highest = math.max(highest, hit.Position.y);
                }
                hits.Dispose();
                Tops[column] = highest;
            }
        }

        /// <summary>Street level: the lowest column top within two columns.</summary>
        [BurstCompile]
        private struct FloorJob : IJobParallelFor
        {
            private const int Reach = 2;
            public int3 Size;
            [ReadOnly] public NativeArray<float> Tops;
            [WriteOnly] public NativeArray<float> Floors;

            public void Execute(int column)
            {
                var x = column % Size.x;
                var z = column / Size.x;
                var lowest = float.MaxValue;
                for (var dz = -Reach; dz <= Reach; dz++)
                for (var dx = -Reach; dx <= Reach; dx++)
                {
                    int nx = x + dx, nz = z + dz;
                    if (nx < 0 || nz < 0 || nx >= Size.x || nz >= Size.z) continue;
                    lowest = math.min(lowest, Tops[nx + Size.x * nz]);
                }
                Floors[column] = lowest;
            }
        }

        /// <summary>Solid = a static collider within half a cell diagonal of the centre.</summary>
        [BurstCompile]
        private struct SolidJob : IJobParallelFor
        {
            [ReadOnly] public CollisionWorld World;
            public float3 Origin;
            public float CellSize;
            public int3 Size;
            [ReadOnly] public NativeArray<float> Tops;
            [WriteOnly] public NativeArray<byte> Cells;

            public void Execute(int index)
            {
                var layer = Size.x * Size.z;
                var y = index / layer;
                var column = index - y * layer;
                var x = column % Size.x;
                var z = column / Size.x;
                var centre = Origin + (new float3(x, y, z) + 0.5f) * CellSize;
                var reach = CellSize * 0.866f;

                if (centre.y - reach > Tops[column])
                {
                    Cells[index] = Free;
                    return;
                }

                var hits = new NativeList<DistanceHit>(4, Allocator.Temp);
                var solid = false;
                var input = new PointDistanceInput { Position = centre, MaxDistance = reach, Filter = CollisionFilter.Default };
                if (World.CalculateDistance(input, ref hits))
                {
                    foreach (var hit in hits)
                    {
                        if (!IsStatic(World, hit.RigidBodyIndex)) continue;
                        solid = true;
                        break;
                    }
                }
                hits.Dispose();
                Cells[index] = solid ? Solid : Free;
            }
        }

        /// <summary>Free cells not reachable from the top layer (6 neighbours) become solid.</summary>
        [BurstCompile]
        private struct FloodJob : IJob
        {
            public int3 Size;
            public NativeArray<byte> Cells;
            public NativeArray<int> Queue;

            public void Execute()
            {
                var layer = Size.x * Size.z;
                int head = 0, tail = 0;
                var topStart = (Size.y - 1) * layer;
                for (var i = topStart; i < topStart + layer; i++)
                {
                    if (Cells[i] != Free) continue;
                    Cells[i] = Reached;
                    Queue[tail++] = i;
                }

                while (head < tail)
                {
                    var index = Queue[head++];
                    var y = index / layer;
                    var column = index - y * layer;
                    var x = column % Size.x;
                    var z = column / Size.x;
                    if (x > 0) Visit(index - 1, ref tail);
                    if (x < Size.x - 1) Visit(index + 1, ref tail);
                    if (z > 0) Visit(index - Size.x, ref tail);
                    if (z < Size.z - 1) Visit(index + Size.x, ref tail);
                    if (y > 0) Visit(index - layer, ref tail);
                    if (y < Size.y - 1) Visit(index + layer, ref tail);
                }

                for (var i = 0; i < Cells.Length; i++)
                    Cells[i] = Cells[i] == Reached ? Free : Solid;
            }

            private void Visit(int index, ref int tail)
            {
                if (Cells[index] != Free) return;
                Cells[index] = Reached;
                Queue[tail++] = index;
            }
        }

        /// <summary>Clearance: cells from the nearest solid cell, by a breadth-first search out from all solid cells.</summary>
        [BurstCompile]
        private struct ClearanceJob : IJob
        {
            public int3 Size;
            /// <summary>In: 0 solid, 1 free. Out: 0 solid, else the clearance (1..255).</summary>
            public NativeArray<byte> Cells;
            public NativeArray<int> Queue;

            public void Execute()
            {
                var layer = Size.x * Size.z;
                int head = 0, tail = 0;
                for (var i = 0; i < Cells.Length; i++)
                {
                    if (Cells[i] == Solid) Queue[tail++] = i;
                    else Cells[i] = 255; // unknown yet
                }

                // First ring: free cells next to solid get 1; then outwards. 255 = not set (or 255 away).
                while (head < tail)
                {
                    var index = Queue[head++];
                    var distance = Cells[index];
                    if (distance == 254) continue;
                    var next = (byte)(distance + 1);
                    var y = index / layer;
                    var column = index - y * layer;
                    var x = column % Size.x;
                    var z = column / Size.x;
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        var ny = y + dy;
                        if (ny < 0 || ny >= Size.y) continue;
                        for (var dz = -1; dz <= 1; dz++)
                        {
                            var nz = z + dz;
                            if (nz < 0 || nz >= Size.z) continue;
                            for (var dx = -1; dx <= 1; dx++)
                            {
                                var nx = x + dx;
                                if (nx < 0 || nx >= Size.x || (dx == 0 && dy == 0 && dz == 0)) continue;
                                var neighbour = nx + Size.x * (nz + Size.z * ny);
                                if (Cells[neighbour] != 255) continue;
                                Cells[neighbour] = next;
                                Queue[tail++] = neighbour;
                            }
                        }
                    }
                }
            }
        }
    }
}
