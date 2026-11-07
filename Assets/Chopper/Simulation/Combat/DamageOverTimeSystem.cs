using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;
using Unity.Physics.Systems;

namespace Chopper.Simulation.Combat
{
    /// <summary>
    /// Server only. Turns each chopper's <see cref="DamageOverTime"/> into <see cref="DamageEvent"/>s every
    /// <see cref="DamageOverTimeRules.PulseSeconds"/> (with the source, so kills count for the pilot who started it), and
    /// keeps <see cref="StatusLook"/> up to date for the clients' effects. A dead chopper's effects end.
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PhysicsSystemGroup), OrderLast = true)]
    [UpdateBefore(typeof(ApplyDamageSystem))]
    public partial struct DamageOverTimeSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var deltaTime = SystemAPI.Time.DeltaTime;
            foreach (var (effects, damage, look, health) in SystemAPI
                         .Query<DynamicBuffer<DamageOverTime>, DynamicBuffer<DamageEvent>, RefRW<StatusLook>, RefRO<Health>>())
            {
                if (effects.Length == 0 || health.ValueRO.IsDead)
                {
                    effects.Clear();
                    if (look.ValueRO.Flags != 0) look.ValueRW.Flags = 0;
                    look.ValueRW.PulseTimer = 0f;
                    continue;
                }

                look.ValueRW.PulseTimer += deltaTime;
                var pulseSeconds = look.ValueRO.PulseTimer;
                var pulse = pulseSeconds >= DamageOverTimeRules.PulseSeconds;
                byte flags = 0;
                for (var i = effects.Length - 1; i >= 0; i--)
                {
                    var effect = effects[i];
                    if (pulse)
                    {
                        var amount = DamageOverTimeRules.Pulse(ref effect, pulseSeconds);
                        if (amount > 0f) damage.Add(new DamageEvent { Amount = amount, Source = effect.Source, Kind = effect.Kind });
                    }
                    if (effect.Remaining <= 0.01f || effect.SecondsLeft <= 0f)
                    {
                        effects.RemoveAt(i);
                        continue;
                    }
                    effects[i] = effect;
                    flags |= effect.Kind == DamageKind.Burn ? StatusLook.Burning : StatusLook.Corroding;
                }
                if (pulse) look.ValueRW.PulseTimer = 0f;
                if (look.ValueRO.Flags != flags) look.ValueRW.Flags = flags;
            }
        }
    }
}
