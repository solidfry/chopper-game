using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace Chopper.Simulation.Session
{
    /// <summary>The map the loaded gameplay subscene belongs to (<see cref="MapCatalog"/> id). Baked from
    /// <see cref="MatchMapAuthoring"/> (setup puts one in every map's gameplay subscene).</summary>
    public struct MatchMap : IComponentData
    {
        public byte MapId;
    }

    /// <summary>Server → client, once per connection: which map this server runs, so an online client loads it.</summary>
    public struct MatchMapAnnouncement : IRpcCommand
    {
        public byte MapId;
    }

    /// <summary>Client: the map the server announced (<see cref="MatchMapAnnouncement"/>). The hangar waits for it
    /// after joining a hosted match, then loads that map's scene.</summary>
    public struct AnnouncedMap : IComponentData
    {
        public byte MapId;
    }

    /// <summary>Server: the connection got its <see cref="MatchMapAnnouncement"/>.</summary>
    public struct MatchMapSent : IComponentData
    {
    }

    /// <summary>Server: tells every new connection the map (as soon as the map's subscene is loaded).</summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct MatchMapAnnounceSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<MatchMap>();
            state.RequireForUpdate(SystemAPI.QueryBuilder().WithAll<NetworkId>().WithNone<MatchMapSent>().Build());
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var map = SystemAPI.GetSingleton<MatchMap>();
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (_, connection) in SystemAPI.Query<RefRO<NetworkId>>().WithNone<MatchMapSent>().WithEntityAccess())
            {
                ecb.AddComponent<MatchMapSent>(connection);
                var rpc = ecb.CreateEntity();
                ecb.AddComponent(rpc, new MatchMapAnnouncement { MapId = map.MapId });
                ecb.AddComponent(rpc, new SendRpcCommandRequest { TargetConnection = connection });
            }
            ecb.Playback(state.EntityManager);
        }
    }

    /// <summary>Client: keeps the announced map as a singleton (<see cref="AnnouncedMap"/>).</summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct MatchMapReceiveSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate(SystemAPI.QueryBuilder().WithAll<MatchMapAnnouncement, ReceiveRpcCommandRequest>().Build());
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (announcement, rpc) in SystemAPI.Query<RefRO<MatchMapAnnouncement>>().WithAll<ReceiveRpcCommandRequest>()
                         .WithEntityAccess())
            {
                var mapId = announcement.ValueRO.MapId;
                if (SystemAPI.TryGetSingletonEntity<AnnouncedMap>(out var existing))
                    ecb.SetComponent(existing, new AnnouncedMap { MapId = mapId });
                else
                    ecb.AddComponent(ecb.CreateEntity(), new AnnouncedMap { MapId = mapId });
                ecb.DestroyEntity(rpc);
            }
            ecb.Playback(state.EntityManager);
        }
    }
}
