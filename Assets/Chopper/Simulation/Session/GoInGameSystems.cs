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
        public ushort PilotId;
        /// <summary>The client's own progress claim (not trusted): see <see cref="Progression.PlayerProgress"/>.</summary>
        public int AccountXp;
        public int ChassisXp;
        public int PilotXp;
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
                    PilotId = choice.PilotId,
                    AccountXp = hasChoice ? chosen.AccountXp : 0,
                    ChassisXp = hasChoice ? chosen.ChassisXp : 0,
                    PilotXp = hasChoice ? chosen.PilotXp : 0,
                });
                ecb.AddComponent(request, new SendRpcCommandRequest { TargetConnection = connection });
                UnityEngine.Debug.Log("[Client] Connected; asking the server for a chopper.");
            }
            ecb.Playback(state.EntityManager);
        }
    }

    /// <summary>
    /// Server: accept the request and ask for a chopper for that connection (a <see cref="RespawnTimer"/> of 0 s).
    /// <see cref="ChopperRespawnSystem"/> picks the spawn point and spawns it, the same way as a respawn.
    /// </summary>
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
                    PilotId = goInGame.ValueRO.PilotId,
                };
                var skinId = goInGame.ValueRO.SkinId;
                ecb.AddComponent(connection, new RequestedLoadout { Code = loadout, SkinId = skinId });
                ecb.AddComponent(connection, new PlayerId { Value = networkId });
                // The client's claim: not trusted. LoadoutGateSystem checks the loadout against it before the spawn.
                ecb.AddComponent(connection, new Progression.PlayerProgress
                {
                    AccountXp = Unity.Mathematics.math.max(0, goInGame.ValueRO.AccountXp),
                    ChassisXp = Unity.Mathematics.math.max(0, goInGame.ValueRO.ChassisXp),
                    PilotXp = Unity.Mathematics.math.max(0, goInGame.ValueRO.PilotXp),
                    Trusted = false,
                });
                ecb.AddComponent(connection, new RespawnTimer { Remaining = 0f });
                UnityEngine.Debug.Log($"[Server] Player {networkId} joined; chopper asked for.");

                ecb.DestroyEntity(requestEntity);
            }
            ecb.Playback(state.EntityManager);
        }
    }
}
