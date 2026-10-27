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
            foreach (var (input, config, countermeasure) in SystemAPI
                         .Query<RefRO<ChopperInput>, RefRO<CountermeasureConfig>, RefRW<CountermeasureState>>()
                         .WithAll<Simulate>())
            {
                ref var flares = ref countermeasure.ValueRW;
                flares.CooldownRemaining = math.max(0f, flares.CooldownRemaining - deltaTime);
                flares.DecoyRemaining = math.max(0f, flares.DecoyRemaining - deltaTime);

                if (!input.ValueRO.Countermeasure.IsSet || flares.CooldownRemaining > 0f) continue;
                flares.CooldownRemaining = config.ValueRO.Cooldown;
                flares.DecoyRemaining = config.ValueRO.DecoyDuration;
                flares.FlareCount++;
            }
        }
    }
}
