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
    [UpdateAfter(typeof(RocketFireSystem))]
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

                if (TryHit(world, rocket.Shooter, rocket.Position, end, out var hit))
                {
                    if (_damageLookup.TryGetBuffer(hit.Entity, out var direct))
                        direct.Add(new DamageEvent { Amount = rocket.DirectDamage, Source = rocket.Shooter });
                    Splash(world, rocket, hit.Position, hit.Entity);
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

        private void Splash(in CollisionWorld world, in Rocket rocket, float3 centre, Entity directTarget)
        {
            if (rocket.SplashRadius <= 0f || rocket.SplashDamage <= 0f) return;

            var hits = new NativeList<DistanceHit>(16, Allocator.Temp);
            var damaged = new NativeHashSet<Entity>(16, Allocator.Temp);
            if (world.OverlapSphere(centre, rocket.SplashRadius, ref hits, CollisionFilter.Default))
            {
                foreach (var hit in hits)
                {
                    var target = hit.Entity;
                    if (target == rocket.Shooter || target == directTarget || !damaged.Add(target)) continue;
                    if (!_damageLookup.TryGetBuffer(target, out var damage)) continue;

                    var falloff = 1f - math.saturate(hit.Distance / rocket.SplashRadius);
                    damage.Add(new DamageEvent { Amount = rocket.SplashDamage * falloff, Source = rocket.Shooter });
                }
            }
            hits.Dispose();
            damaged.Dispose();
        }

        private static bool TryHit(in CollisionWorld world, Entity shooter, float3 start, float3 end, out RaycastHit closest)
        {
            var ray = new RaycastInput { Start = start, End = end, Filter = CollisionFilter.Default };
            var hits = new NativeList<RaycastHit>(8, Allocator.Temp);
            closest = default;
            var found = false;

            if (world.CastRay(ray, ref hits))
            {
                foreach (var hit in hits)
                {
                    if (hit.Entity == shooter || (found && hit.Fraction >= closest.Fraction)) continue;
                    closest = hit;
                    found = true;
                }
            }
            hits.Dispose();
            return found;
        }
    }
}
