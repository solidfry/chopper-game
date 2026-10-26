using Chopper.Simulation.Flight;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;

namespace Chopper.Simulation.Combat
{
    /// <summary>
    /// Hitscan with lag compensation ("favour the shooter, within limits"):
    /// <list type="bullet">
    /// <item>Client and server both run the fire rate (predicted), so shots line up with the server.</item>
    /// <item>The server ray-casts against the physics world as the shooter saw it: current tick minus the
    /// client's interpolation delay, capped at <see cref="MaxRewindSeconds"/>.</item>
    /// <item>Hits become <see cref="DamageEvent"/>s; <see cref="ApplyDamageSystem"/> applies them.</item>
    /// </list>
    /// Lag compensation needs a <c>NetCodePhysicsConfig</c> with Lag Compensation on in the gameplay subscene.
    /// Without it the server ray-casts against the current world (no rewind).
    /// </summary>
    [BurstCompile]
    // Not in the local (menu) world: combat needs client/server worlds.
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PhysicsSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(ExportPhysicsWorld))]
    public partial struct HitscanFireSystem : ISystem
    {
        /// <summary>Players with more ping than this must lead their targets. Stops "shot behind cover".</summary>
        private const float MaxRewindSeconds = 0.2f;
        private const int DefaultTickRate = 60;

        private ComponentLookup<CommandDataInterpolationDelay> _interpolationDelayLookup;
        private BufferLookup<DamageEvent> _damageLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkTime>();
            state.RequireForUpdate<PhysicsWorldSingleton>();
            _interpolationDelayLookup = state.GetComponentLookup<CommandDataInterpolationDelay>(isReadOnly: true);
            _damageLookup = state.GetBufferLookup<DamageEvent>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var deltaTime = SystemAPI.Time.DeltaTime;
            var isServer = state.WorldUnmanaged.IsServer();
            var serverTick = SystemAPI.GetSingleton<NetworkTime>().ServerTick;
            var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().PhysicsWorld;
            var hasHistory = SystemAPI.TryGetSingleton<PhysicsWorldHistorySingleton>(out var history);
            var tickRate = SystemAPI.TryGetSingleton<ClientServerTickRate>(out var rates) && rates.SimulationTickRate > 0
                ? rates.SimulationTickRate
                : DefaultTickRate;
            var maxRewindTicks = (uint)math.round(MaxRewindSeconds * tickRate);

            _interpolationDelayLookup.Update(ref state);
            _damageLookup.Update(ref state);

            foreach (var (input, config, weapon, transform, shooter) in SystemAPI
                         .Query<RefRO<ChopperInput>, RefRO<HitscanWeaponConfig>, RefRW<HitscanWeaponState>, RefRO<LocalTransform>>()
                         .WithAll<Simulate>()
                         .WithEntityAccess())
            {
                ref var weaponState = ref weapon.ValueRW;
                weaponState.CooldownRemaining = math.max(0f, weaponState.CooldownRemaining - deltaTime);
                if (!input.ValueRO.Fire || weaponState.CooldownRemaining > 0f) continue;
                weaponState.CooldownRemaining = config.ValueRO.FireInterval;
                weaponState.ShotCount++;

                // Clients only predict the fire rate and shot count; presentation draws tracers from the count.
                if (!isServer) continue;

                var collisionWorld = physicsWorld.CollisionWorld;
                if (hasHistory)
                {
                    var delay = _interpolationDelayLookup.TryGetComponent(shooter, out var interpolation)
                        ? math.min(interpolation.Delay, maxRewindTicks)
                        : 0u;
                    history.GetCollisionWorldFromTick(serverTick, delay, ref physicsWorld, out collisionWorld);
                }

                var pose = transform.ValueRO;
                var muzzle = pose.Position + math.mul(pose.Rotation, config.ValueRO.MuzzleOffset);
                var forward = math.mul(pose.Rotation, math.forward());
                if (!TryHit(collisionWorld, shooter, muzzle, muzzle + forward * config.ValueRO.Range, out var target))
                    continue;

                if (_damageLookup.TryGetBuffer(target, out var damage))
                    damage.Add(new DamageEvent { Amount = config.ValueRO.Damage, Source = shooter });
            }
        }

        /// <summary>Closest hit along the ray that is not the shooter.</summary>
        private static bool TryHit(in CollisionWorld world, Entity shooter, float3 start, float3 end, out Entity target)
        {
            var ray = new RaycastInput { Start = start, End = end, Filter = CollisionFilter.Default };
            var hits = new NativeList<RaycastHit>(8, Allocator.Temp);
            var closest = float.MaxValue;
            target = Entity.Null;

            if (world.CastRay(ray, ref hits))
            {
                foreach (var hit in hits)
                {
                    if (hit.Entity == shooter || hit.Fraction >= closest) continue;
                    closest = hit.Fraction;
                    target = hit.Entity;
                }
            }
            hits.Dispose();
            return target != Entity.Null;
        }
    }
}
