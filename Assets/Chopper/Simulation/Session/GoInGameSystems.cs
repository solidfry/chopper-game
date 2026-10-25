using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace Chopper.Simulation.Session
{
    /// <summary>Sent by a client when it is ready to play.</summary>
    public struct GoInGameRequest : IRpcCommand
    {
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
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (_, connection) in SystemAPI.Query<RefRO<NetworkId>>().WithNone<NetworkStreamInGame>().WithEntityAccess())
            {
                ecb.AddComponent<NetworkStreamInGame>(connection);
                var request = ecb.CreateEntity();
                ecb.AddComponent<GoInGameRequest>(request);
                ecb.AddComponent(request, new SendRpcCommandRequest { TargetConnection = connection });
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
            var spawner = SystemAPI.GetSingleton<ChopperSpawner>();
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (request, requestEntity) in SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>>()
                         .WithAll<GoInGameRequest>().WithEntityAccess())
            {
                var connection = request.ValueRO.SourceConnection;
                var networkId = SystemAPI.GetComponent<NetworkId>(connection).Value;
                ecb.AddComponent<NetworkStreamInGame>(connection);

                var right = math.mul(spawner.Rotation, math.right());
                var position = spawner.Position + right * (spawner.Spacing * (networkId - 1));

                var chopper = ecb.Instantiate(spawner.Prefab);
                ecb.SetComponent(chopper, LocalTransform.FromPositionRotation(position, spawner.Rotation));
                ecb.SetComponent(chopper, new GhostOwner { NetworkId = networkId });
                // Despawn the chopper when the connection goes away.
                ecb.AppendToBuffer(connection, new LinkedEntityGroup { Value = chopper });

                ecb.DestroyEntity(requestEntity);
            }
            ecb.Playback(state.EntityManager);
        }
    }
}
