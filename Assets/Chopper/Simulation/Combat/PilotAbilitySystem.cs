using Chopper.Simulation.Flight;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics.Systems;
using Unity.Transforms;

namespace Chopper.Simulation.Combat
{
    /// <summary>
    /// Pilot abilities (plan section 5b, step 4). Predicted on the owner, decided on the server, like flares: the key
    /// (<see cref="ChopperInput.Ability"/>) starts the pilot's ability (<see cref="Abilities.TryActivate"/>), timers
    /// count down. Ghost and Strip Down open the flare decoy window (Strip Down also drops flares). Server only: Nanite
    /// Cloud heals; Blackout jams every other chopper in reach (their locks drop, <see cref="TargetLockSystem"/>).
    /// Other effects are read where they act: endless boost (flight), shield, crash immunity and damage-over-time scale
    /// (<see cref="ApplyDamageSystem"/>), Ping (HUD markers).
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PhysicsSystemGroup), OrderLast = true)]
    // TargetLockSystem (server only) orders itself after this one: an attribute here would point at a system the
    // client world does not have.
    [UpdateAfter(typeof(CountermeasureSystem))]
    public partial struct PilotAbilitySystem : ISystem
    {
        private struct Pulse
        {
            public Entity Source;
            public float3 Position;
            public float Radius;
            public float Seconds;
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var deltaTime = SystemAPI.Time.DeltaTime;
            var isServer = state.WorldUnmanaged.IsServer();
            var pulses = new NativeList<Pulse>(2, Allocator.Temp);

            foreach (var (input, config, ability, countermeasure, transform, entity) in SystemAPI
                         .Query<RefRO<ChopperInput>, RefRO<AbilityConfig>, RefRW<AbilityState>, RefRW<CountermeasureState>,
                             RefRO<LocalTransform>>()
                         .WithAll<Simulate>()
                         .WithEntityAccess())
            {
                ref var now = ref ability.ValueRW;
                Abilities.Tick(ref now, deltaTime);
                if (!input.ValueRO.Ability.IsSet || !Abilities.TryActivate(ref now, config.ValueRO)) continue;

                var cfg = config.ValueRO;
                if (cfg.Kind is AbilityKind.Ghost or AbilityKind.StripDown)
                {
                    ref var flares = ref countermeasure.ValueRW;
                    flares.DecoyRemaining = math.max(flares.DecoyRemaining, now.ActiveLeft);
                    if (cfg.Kind == AbilityKind.StripDown) flares.FlareCount++;
                }
                if (isServer && cfg.Kind == AbilityKind.Blackout)
                    pulses.Add(new Pulse { Source = entity, Position = transform.ValueRO.Position, Radius = cfg.Radius, Seconds = cfg.Strength });
            }

            if (isServer)
            {
                // Nanite Cloud: the server owns health.
                foreach (var (config, ability, health) in SystemAPI.Query<RefRO<AbilityConfig>, RefRO<AbilityState>, RefRW<Health>>())
                {
                    var rate = Abilities.HealPerSecond(config.ValueRO, ability.ValueRO, health.ValueRO.Max);
                    if (rate <= 0f || health.ValueRO.IsDead) continue;
                    health.ValueRW.Current = math.min(health.ValueRO.Max, health.ValueRO.Current + rate * deltaTime);
                }

                foreach (var pulse in pulses)
                foreach (var (ability, transform, lockState, entity) in SystemAPI
                             .Query<RefRW<AbilityState>, RefRO<LocalTransform>, RefRW<TargetLock>>()
                             .WithEntityAccess())
                {
                    if (entity == pulse.Source || math.distancesq(transform.ValueRO.Position, pulse.Position) > pulse.Radius * pulse.Radius)
                        continue;
                    ability.ValueRW.JammedLeft = math.max(ability.ValueRO.JammedLeft, pulse.Seconds);
                    lockState.ValueRW = default;
                }
            }
            pulses.Dispose();
        }
    }
}
