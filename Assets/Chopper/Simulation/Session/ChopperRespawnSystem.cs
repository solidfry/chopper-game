using Chopper.Simulation.Combat;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace Chopper.Simulation.Session
{
    /// <summary>
    /// Server only. A chopper at zero health is destroyed (the ghost despawns on all clients). Its player gets a new
    /// chopper after <see cref="ChopperSpawner.RespawnDelay"/>.
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
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

            foreach (var (timer, networkId, connection) in SystemAPI.Query<RefRW<RespawnTimer>, RefRO<NetworkId>>().WithEntityAccess())
            {
                timer.ValueRW.Remaining -= deltaTime;
                if (timer.ValueRO.Remaining > 0f) continue;

                var loadout = SystemAPI.HasComponent<RequestedLoadout>(connection)
                    ? SystemAPI.GetComponent<RequestedLoadout>(connection).Code
                    : default;
                ChopperSpawning.Spawn(ecb, spawner, chassisPrefabs, connection, networkId.ValueRO.Value, loadout);
                ecb.RemoveComponent<RespawnTimer>(connection);
            }

            ecb.Playback(state.EntityManager);
        }
    }
}
