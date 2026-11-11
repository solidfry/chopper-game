using Chopper.Simulation.Flight;
using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;
using Unity.Physics.Systems;

namespace Chopper.Simulation.Combat
{
    /// <summary>
    /// Server only. Field repair (<see cref="Repair"/>): while the pilot holds the repair key the hull mends; damage this
    /// tick has already stopped it (<see cref="ApplyDamageSystem"/> runs first and locks the repair).
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PhysicsSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(ApplyDamageSystem))]
    public partial struct RepairSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var deltaTime = SystemAPI.Time.DeltaTime;
            var hasLog = SystemAPI.TryGetSingletonBuffer<Session.MatchLogEntry>(out var log);
            var now = SystemAPI.Time.ElapsedTime;
            foreach (var (repair, config, health, input, chopper) in SystemAPI
                         .Query<RefRW<ChopperRepair>, RefRO<RepairConfig>, RefRW<Health>, RefRO<ChopperInput>>()
                         .WithEntityAccess())
            {
                ref var current = ref repair.ValueRW;
                current.Healed += Repair.Step(ref current, config.ValueRO, ref health.ValueRW, input.ValueRO.Repair, deltaTime);
                // One log entry when a repair stops (done, let go, shot): the health it mended.
                if (current.State == RepairPhase.Repairing || current.Healed <= 0f) continue;
                if (hasLog)
                    log.Add(new Session.MatchLogEntry
                    {
                        Kind = Session.MatchLogKind.Repair,
                        Time = now,
                        Actor = SystemAPI.HasComponent<GhostOwner>(chopper) ? SystemAPI.GetComponent<GhostOwner>(chopper).NetworkId : 0,
                        Amount = current.Healed,
                        Distance = -1f,
                    });
                current.Healed = 0f;
            }
        }
    }
}
