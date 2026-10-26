using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;

namespace Chopper.Simulation.Combat
{
    /// <summary>Server only: brings destroyed <see cref="TrainingTarget"/>s back to full health.</summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct TrainingTargetSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var deltaTime = SystemAPI.Time.DeltaTime;
            foreach (var (health, target) in SystemAPI.Query<RefRW<Health>, RefRW<TrainingTarget>>())
            {
                if (!health.ValueRO.IsDead) continue;

                if (target.ValueRO.ResetRemaining <= 0f)
                    target.ValueRW.ResetRemaining = target.ValueRO.ResetDelay;

                target.ValueRW.ResetRemaining -= deltaTime;
                if (target.ValueRO.ResetRemaining > 0f) continue;

                target.ValueRW.ResetRemaining = 0f;
                health.ValueRW.Current = health.ValueRO.Max;
            }
        }
    }
}
