using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Chopper.Simulation.Loadout;

namespace Chopper.Simulation.Session
{
    /// <summary>Sent by a client when it is ready to play, with the loadout it wants (empty = default).</summary>
    public struct GoInGameRequest : IRpcCommand
    {
        public ushort ChassisId;
        public ulong Modules0;
        public ulong Modules1;
        public ushort SkinId;
    }

    /// <summary>Client: once connected, mark the connection in game and ask the server for a chopper.</summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ThinClientSimulation)]
    public partial struct GoInGameClientSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ChopperSpawner>();
            state.RequireForUpdate(SystemAPI.QueryBuilder().WithAll<NetworkId>().WithNone<NetworkStreamInGame>().Build());
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var hasChoice = SystemAPI.TryGetSingleton<LoadoutChoice>(out var chosen);
            var choice = hasChoice ? chosen.Code : default;
            var skin = hasChoice ? chosen.SkinId : (ushort)0;
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (_, connection) in SystemAPI.Query<RefRO<NetworkId>>().WithNone<NetworkStreamInGame>().WithEntityAccess())
            {
                ecb.AddComponent<NetworkStreamInGame>(connection);
                var request = ecb.CreateEntity();
                ecb.AddComponent(request, new GoInGameRequest
                {
                    ChassisId = choice.ChassisId,
                    Modules0 = choice.Modules0,
                    Modules1 = choice.Modules1,
                    SkinId = skin,
                });
                ecb.AddComponent(request, new SendRpcCommandRequest { TargetConnection = connection });
                UnityEngine.Debug.Log("[Client] Connected; asking the server for a chopper.");
            }
            ecb.Playback(state.EntityManager);
        }
    }

    /// <summary>Server: accept the request, spawn a chopper owned by that connection.</summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct GoInGameServerSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ChopperSpawner>();
            state.RequireForUpdate(SystemAPI.QueryBuilder().WithAll<GoInGameRequest, ReceiveRpcCommandRequest>().Build());
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var spawnerEntity = SystemAPI.GetSingletonEntity<ChopperSpawner>();
            var spawner = SystemAPI.GetComponent<ChopperSpawner>(spawnerEntity);
            var chassisPrefabs = SystemAPI.GetBuffer<ChassisPrefab>(spawnerEntity);
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (request, goInGame, requestEntity) in SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<GoInGameRequest>>()
                         .WithEntityAccess())
            {
                var connection = request.ValueRO.SourceConnection;
                var networkId = SystemAPI.GetComponent<NetworkId>(connection).Value;
                ecb.AddComponent<NetworkStreamInGame>(connection);

                var loadout = new LoadoutCode
                {
                    ChassisId = goInGame.ValueRO.ChassisId,
                    Modules0 = goInGame.ValueRO.Modules0,
                    Modules1 = goInGame.ValueRO.Modules1,
                };
                var skinId = goInGame.ValueRO.SkinId;
                ecb.AddComponent(connection, new RequestedLoadout { Code = loadout, SkinId = skinId });
                ChopperSpawning.Spawn(ecb, spawner, chassisPrefabs, connection, networkId, loadout, skinId);
                UnityEngine.Debug.Log($"[Server] Player {networkId} joined; chopper spawned.");

                ecb.DestroyEntity(requestEntity);
            }
            ecb.Playback(state.EntityManager);
        }
    }
}
