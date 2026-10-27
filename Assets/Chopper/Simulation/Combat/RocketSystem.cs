using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Physics.Systems;

namespace Chopper.Simulation.Combat
{
    /// <summary>
    /// Server only. Moves rockets one ray segment per tick. On impact: direct damage to what it hit, splash damage
    /// (falling off with distance) to every other damageable body in the radius. The shooter takes no damage.
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PhysicsSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(PylonFireSystem))]
    [UpdateBefore(typeof(ApplyDamageSystem))]
    public partial struct RocketSystem : ISystem
    {
        private BufferLookup<DamageEvent> _damageLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PhysicsWorldSingleton>();
            _damageLookup = state.GetBufferLookup<DamageEvent>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var deltaTime = SystemAPI.Time.DeltaTime;
            var world = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
            _damageLookup.Update(ref state);
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (rocketRef, entity) in SystemAPI.Query<RefRW<Rocket>>().WithEntityAccess())
            {
                ref var rocket = ref rocketRef.ValueRW;
                var end = rocket.Position + rocket.Velocity * deltaTime;

                if (Explosions.TryRaycast(world, rocket.Shooter, rocket.Position, end, out var hit))
                {
                    if (_damageLookup.TryGetBuffer(hit.Entity, out var direct))
                        direct.Add(new DamageEvent { Amount = rocket.DirectDamage, Source = rocket.Shooter });
                    Explosions.Splash(world, ref _damageLookup, hit.Position, rocket.SplashRadius, rocket.SplashDamage, rocket.Shooter, hit.Entity);
                    ecb.DestroyEntity(entity);
                    continue;
                }

                rocket.Position = end;
                rocket.Remaining -= deltaTime;
                if (rocket.Remaining <= 0f)
                    ecb.DestroyEntity(entity);
            }
            ecb.Playback(state.EntityManager);
        }
    }
}
