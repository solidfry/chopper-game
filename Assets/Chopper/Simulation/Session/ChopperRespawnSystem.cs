using Chopper.Simulation.Combat;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Transforms;

namespace Chopper.Simulation.Session
{
    /// <summary>
    /// Server only. The one place that spawns choppers:
    /// <list type="bullet">
    /// <item>A chopper at zero health is destroyed (the ghost despawns on all clients). Its player gets a
    /// <see cref="RespawnTimer"/> of <see cref="ChopperSpawner.RespawnDelay"/>.</item>
    /// <item>A new player gets a timer of 0 s (<see cref="GoInGameServerSystem"/>).</item>
    /// <item>When a timer ends, the player's chopper spawns at the best <see cref="SpawnPoint"/> of that moment
    /// (<see cref="SpawnScoring"/>: away from enemies, walls and the ground; rules in <see cref="SpawnRules"/>). If no
    /// point can be used (or the map has none), it spawns at the spawner's own point.</item>
    /// </list>
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(GoInGameServerSystem))]
    public partial struct ChopperRespawnSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ChopperSpawner>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var spawnerEntity = SystemAPI.GetSingletonEntity<ChopperSpawner>();
            var spawner = SystemAPI.GetComponent<ChopperSpawner>(spawnerEntity);
            var chassisPrefabs = SystemAPI.GetBuffer<ChassisPrefab>(spawnerEntity);
            var deltaTime = SystemAPI.Time.DeltaTime;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (health, owner, chopper) in SystemAPI.Query<RefRO<Health>, RefRO<ConnectionOwner>>().WithEntityAccess())
            {
                if (!health.ValueRO.IsDead) continue;

                var connection = owner.ValueRO.Value;
                if (SystemAPI.HasBuffer<LinkedEntityGroup>(connection))
                {
                    // Take the chopper out of the connection's group so the group never holds a destroyed entity.
                    var group = SystemAPI.GetBuffer<LinkedEntityGroup>(connection);
                    for (var i = group.Length - 1; i >= 0; i--)
                    {
                        if (group[i].Value == chopper)
                            group.RemoveAt(i);
                    }
                    ecb.AddComponent(connection, new RespawnTimer { Remaining = spawner.RespawnDelay });
                }
                ecb.DestroyEntity(chopper);
            }

            // Built on the first spawn of this tick: most ticks have none.
            var placement = default(SpawnPlacement);
            var prepared = false;

            foreach (var (timer, networkId, connection) in SystemAPI.Query<RefRW<RespawnTimer>, RefRO<NetworkId>>().WithEntityAccess())
            {
                timer.ValueRW.Remaining -= deltaTime;
                if (timer.ValueRO.Remaining > 0f) continue;

                if (!prepared)
                {
                    placement = Prepare(ref state, spawnerEntity);
                    prepared = true;
                }

                var id = networkId.ValueRO.Value;
                var position = ChopperSpawning.FallbackPosition(spawner, id);
                var rotation = spawner.Rotation;
                var pick = placement.Choose(ref position, ref rotation);
                if (pick == SpawnPick.None && placement.Points.Length > 0)
                    UnityEngine.Debug.LogWarning($"[Spawn] Player {id}: no spawn point can be used; the spawner's own point is used.");
                else if (pick == SpawnPick.Relaxed)
                    UnityEngine.Debug.Log($"[Spawn] Player {id}: every spawn point has an enemy near; the best of them is used.");

                var requested = SystemAPI.HasComponent<RequestedLoadout>(connection)
                    ? SystemAPI.GetComponent<RequestedLoadout>(connection)
                    : default;
                ChopperSpawning.Spawn(ecb, spawner, chassisPrefabs, connection, id, requested.Code, requested.SkinId, position, rotation);
                ecb.RemoveComponent<RespawnTimer>(connection);
            }

            if (prepared)
            {
                // Remember which points were used (the reuse penalty of later spawns).
                for (var i = 0; i < placement.Points.Length; i++)
                    if (placement.Used[i]) SystemAPI.SetComponent(placement.Points[i], new SpawnPointUse { LastUsed = placement.Now });
                placement.Dispose();
            }

            ecb.Playback(state.EntityManager);
        }

        /// <summary>The map's spawn points measured against the level, and the live choppers.</summary>
        private SpawnPlacement Prepare(ref SystemState state, Entity spawnerEntity)
        {
            var rules = SystemAPI.HasComponent<SpawnRules>(spawnerEntity)
                ? SystemAPI.GetComponent<SpawnRules>(spawnerEntity)
                : SpawnRules.Default;
            var hasBounds = SystemAPI.TryGetSingleton<ArenaBounds>(out var bounds);
            var hasWorld = SystemAPI.TryGetSingleton<PhysicsWorldSingleton>(out var physics);
            // The physics world is written by jobs: wait for them before the queries.
            if (hasWorld) state.CompleteDependency();
            var now = SystemAPI.Time.ElapsedTime;

            var placement = new SpawnPlacement
            {
                Rules = rules,
                HasWorld = hasWorld,
                World = hasWorld ? physics.CollisionWorld : default,
                Now = now,
                Points = new NativeList<Entity>(16, Allocator.Temp),
                Places = new NativeList<SpawnPoint>(16, Allocator.Temp),
                Candidates = new NativeList<SpawnCandidate>(16, Allocator.Temp),
                Used = new NativeList<bool>(16, Allocator.Temp),
                Threats = new NativeList<SpawnThreat>(16, Allocator.Temp),
            };

            foreach (var (point, use, entity) in SystemAPI.Query<RefRO<SpawnPoint>, RefRO<SpawnPointUse>>().WithEntityAccess())
            {
                var candidate = SpawnProbe.Level(point.ValueRO, hasWorld, placement.World, hasBounds, bounds, rules);
                candidate.SecondsSinceUse = (float)math.min(now - use.ValueRO.LastUsed, 1.0e6);
                placement.Points.Add(entity);
                placement.Places.Add(point.ValueRO);
                placement.Candidates.Add(candidate);
                placement.Used.Add(false);
            }

            // Free for all: every live chopper is an enemy. Teams: set Enemy from the two players' teams here.
            foreach (var (health, transform) in SystemAPI.Query<RefRO<Health>, RefRO<LocalTransform>>().WithAll<ConnectionOwner>())
            {
                if (health.ValueRO.IsDead) continue;
                placement.Threats.Add(new SpawnThreat { Position = transform.ValueRO.Position, Enemy = true });
            }
            return placement;
        }

        /// <summary>One tick's spawn work: the points, what is known about them, and the choppers to keep away from.</summary>
        private struct SpawnPlacement
        {
            public SpawnRules Rules;
            public bool HasWorld;
            public CollisionWorld World;
            public double Now;
            public NativeList<Entity> Points;
            public NativeList<SpawnPoint> Places;
            public NativeList<SpawnCandidate> Candidates;
            public NativeList<bool> Used;
            public NativeList<SpawnThreat> Threats;

            /// <summary>
            /// Sets the place of the best point (no change when none can be used). The chosen point counts as a chopper
            /// for the next spawn of the same tick, so two players never get the same or a near point.
            /// </summary>
            public SpawnPick Choose(ref float3 position, ref quaternion rotation)
            {
                for (var i = 0; i < Candidates.Length; i++)
                {
                    var candidate = Candidates[i];
                    SpawnProbe.Threats(ref candidate, Places[i].Position, Threats.AsArray(), HasWorld, World, Rules);
                    Candidates[i] = candidate;
                }

                var best = SpawnScoring.Pick(Candidates.AsArray(), Rules, out var pick);
                if (best < 0) return pick;

                position = Places[best].Position;
                rotation = Places[best].Rotation;
                Used[best] = true;
                var used = Candidates[best];
                used.SecondsSinceUse = 0f;
                Candidates[best] = used;
                Threats.Add(new SpawnThreat { Position = position, Enemy = true });
                return pick;
            }

            public void Dispose()
            {
                Points.Dispose();
                Places.Dispose();
                Candidates.Dispose();
                Used.Dispose();
                Threats.Dispose();
            }
        }
    }
}
