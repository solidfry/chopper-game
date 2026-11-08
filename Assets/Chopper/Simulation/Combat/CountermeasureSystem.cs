using Chopper.Simulation.Flight;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics.Systems;

namespace Chopper.Simulation.Combat
{
    /// <summary>
    /// Flares (predicted on the owner, decided on the server). Dropping them starts the cooldown and a decoy window:
    /// the chopper cannot be heat-locked, and heat seekers chasing it lose it (<see cref="GuidedMissileSystem"/>).
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PhysicsSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(ExportPhysicsWorld))]
    public partial struct CountermeasureSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var deltaTime = SystemAPI.Time.DeltaTime;
            var now = SystemAPI.Time.ElapsedTime;
            var hasLog = SystemAPI.TryGetSingletonBuffer<Session.MatchLogEntry>(out var log) && state.WorldUnmanaged.IsServer();
            foreach (var (input, config, countermeasure, chopper) in SystemAPI
                         .Query<RefRO<ChopperInput>, RefRO<CountermeasureConfig>, RefRW<CountermeasureState>>()
                         .WithAll<Simulate>()
                         .WithEntityAccess())
            {
                ref var flares = ref countermeasure.ValueRW;
                flares.CooldownRemaining = math.max(0f, flares.CooldownRemaining - deltaTime);
                flares.DecoyRemaining = math.max(0f, flares.DecoyRemaining - deltaTime);
                Magazine.Tick(ref flares.RoundsUsed, ref flares.ReloadRemaining, deltaTime);

                // Cooldown 0 = this loadout has no flares.
                if (config.ValueRO.Cooldown <= 0f || !input.ValueRO.Countermeasure.IsSet || flares.CooldownRemaining > 0f) continue;
                if (!Magazine.HasRound(flares.RoundsUsed, config.ValueRO.MagazineSize)) continue;
                flares.CooldownRemaining = config.ValueRO.Cooldown;
                flares.DecoyRemaining = config.ValueRO.DecoyDuration;
                flares.FlareCount++;
                Magazine.Spend(ref flares.RoundsUsed, ref flares.ReloadRemaining, config.ValueRO.MagazineSize, config.ValueRO.ReloadSeconds);
                if (hasLog)
                    log.Add(new Session.MatchLogEntry
                    {
                        Kind = Session.MatchLogKind.Flare,
                        Time = now,
                        Actor = SystemAPI.HasComponent<GhostOwner>(chopper) ? SystemAPI.GetComponent<GhostOwner>(chopper).NetworkId : 0,
                        ActorPosition = SystemAPI.HasComponent<Unity.Transforms.LocalTransform>(chopper)
                            ? SystemAPI.GetComponent<Unity.Transforms.LocalTransform>(chopper).Position
                            : float3.zero,
                        Distance = -1f,
                    });
            }
        }
    }
}
