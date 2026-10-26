using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;

namespace Chopper.Simulation.Combat
{
    /// <summary>
    /// Server only. Crash and out-of-bounds damage, computed from server physics (the client cannot report it).
    /// Runs after the physics step, before <see cref="ApplyDamageSystem"/>.
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PhysicsSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(ExportPhysicsWorld))]
    [UpdateBefore(typeof(ApplyDamageSystem))]
    public partial struct EnvironmentDamageSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var deltaTime = SystemAPI.Time.DeltaTime;
            var hasBounds = SystemAPI.TryGetSingleton<ArenaBounds>(out var bounds);

            foreach (var (velocity, config, sensor, transform, damage) in SystemAPI
                         .Query<RefRO<PhysicsVelocity>, RefRO<CrashDamageConfig>, RefRW<CrashSensor>, RefRO<LocalTransform>,
                             DynamicBuffer<DamageEvent>>()
                         .WithAll<Simulate>())
            {
                // Speed loss only: a dash adds speed, so it never counts as a crash.
                var speed = math.length(velocity.ValueRO.Linear);
                var speedLoss = sensor.ValueRO.PreviousSpeed - speed;
                sensor.ValueRW.PreviousSpeed = speed;

                var crash = speedLoss - config.ValueRO.SafeSpeedLoss;
                if (crash > 0f)
                    damage.Add(new DamageEvent { Amount = crash * config.ValueRO.DamagePerSpeedLoss, Source = Entity.Null });

                if (hasBounds && !bounds.Contains(transform.ValueRO.Position))
                    damage.Add(new DamageEvent { Amount = bounds.DamagePerSecond * deltaTime, Source = Entity.Null });
            }
        }
    }
}
