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
            foreach (var (repair, config, health, input) in SystemAPI
                         .Query<RefRW<ChopperRepair>, RefRO<RepairConfig>, RefRW<Health>, RefRO<ChopperInput>>())
                Repair.Step(ref repair.ValueRW, config.ValueRO, ref health.ValueRW, input.ValueRO.Repair, deltaTime);
        }
    }
}
