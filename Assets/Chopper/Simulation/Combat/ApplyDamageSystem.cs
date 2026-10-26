using Chopper.Simulation.Session;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace Chopper.Simulation.Combat
{
    /// <summary>
    /// Server only. Applies this tick's <see cref="DamageEvent"/>s to <see cref="Health"/> and tells each shooter
    /// that it hit (<see cref="HitConfirmRpc"/>). Death is handled by <see cref="ChopperRespawnSystem"/>.
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PredictedFixedStepSimulationSystemGroup))]
    [UpdateAfter(typeof(HitscanFireSystem))]
    public partial struct ApplyDamageSystem : ISystem
    {
        private ComponentLookup<ConnectionOwner> _connectionLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _connectionLookup = state.GetComponentLookup<ConnectionOwner>(isReadOnly: true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _connectionLookup.Update(ref state);
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (health, damage) in SystemAPI.Query<RefRW<Health>, DynamicBuffer<DamageEvent>>())
            {
                foreach (var hit in damage)
                {
                    if (health.ValueRO.IsDead) break;
                    var killed = health.ValueRW.TakeDamage(hit.Amount);

                    if (!_connectionLookup.TryGetComponent(hit.Source, out var shooter)) continue;
                    var rpc = ecb.CreateEntity();
                    ecb.AddComponent(rpc, new HitConfirmRpc { Damage = hit.Amount, Killed = killed });
                    ecb.AddComponent(rpc, new SendRpcCommandRequest { TargetConnection = shooter.Value });
                }
                damage.Clear();
            }
            ecb.Playback(state.EntityManager);
        }
    }
}
