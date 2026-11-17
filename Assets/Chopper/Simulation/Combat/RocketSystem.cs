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
    /// Server only. Moves rockets one ray segment per tick (<see cref="RocketFlight"/>: gravity and drag bend the
    /// path). On impact: direct damage to what it hit, splash damage (falling off with distance) to every other
    /// damageable body in the radius. A rocket with a proximity fuse also explodes when it passes a live target close
    /// enough (splash only). The shooter takes no damage and does not set off its own fuse.
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
            var owners = SystemAPI.GetComponentLookup<GhostOwner>(true);
            var hasLog = SystemAPI.TryGetSingletonBuffer<Session.MatchLogEntry>(out var log);
            var now = SystemAPI.Time.ElapsedTime;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // What a proximity fuse can react to: live targets, this tick's positions.
            var fuseTargets = new NativeList<FuseTarget>(16, Allocator.Temp);
            foreach (var (targetable, health, pose, target) in SystemAPI
                         .Query<RefRO<Targetable>, RefRO<Health>, RefRO<LocalTransform>>()
                         .WithEntityAccess())
            {
                if (health.ValueRO.IsDead) continue;
                fuseTargets.Add(new FuseTarget { Entity = target, Position = pose.ValueRO.Position, Class = targetable.ValueRO.Class });
            }

            foreach (var (rocketRef, entity) in SystemAPI.Query<RefRW<Rocket>>().WithEntityAccess())
            {
                ref var rocket = ref rocketRef.ValueRW;
                var end = RocketFlight.Step(rocket.Position, ref rocket.Velocity, rocket.Gravity, rocket.Drag, deltaTime);

                if (Explosions.TryRaycast(world, rocket.Shooter, rocket.Position, end, out var hit))
                {
                    if (_damageLookup.TryGetBuffer(hit.Entity, out var direct))
                        direct.Add(new DamageEvent
                        {
                            Amount = rocket.DirectDamage,
                            Source = rocket.Shooter,
                            Point = hit.Position,
                            Normal = hit.SurfaceNormal,
                            Kind = DamageKind.Explosion,
                            Weapon = rocket.Weapon,
                            Attacker = rocket.Attacker,
                        });
                    Explosions.Splash(world, ref _damageLookup, hit.Position, rocket.SplashRadius, rocket.SplashDamage, rocket.Shooter, hit.Entity,
                        rocket.Weapon, rocket.Attacker);
                    if (hit.Entity != rocket.Threat && TryFind(fuseTargets, rocket.Threat, out var threatAt) &&
                        RocketEvade.BurstMissed(hit.Position, threatAt, rocket.SplashRadius))
                        Evaded(ref rocket, owners, hasLog, log, now, threatAt);
                    ecb.DestroyEntity(entity);
                    continue;
                }

                if (rocket.ProximityFuse > 0f && TryFuse(fuseTargets, rocket, end, out var burst))
                {
                    Explosions.Splash(world, ref _damageLookup, burst, rocket.SplashRadius, rocket.SplashDamage, rocket.Shooter, Entity.Null,
                        rocket.Weapon, rocket.Attacker);
                    if (TryFind(fuseTargets, rocket.Threat, out var fusedAt) && RocketEvade.BurstMissed(burst, fusedAt, rocket.SplashRadius))
                        Evaded(ref rocket, owners, hasLog, log, now, fusedAt);
                    ecb.DestroyEntity(entity);
                    continue;
                }

                Watch(ref rocket, fuseTargets, end, owners, hasLog, log, now);
                rocket.Position = end;
                rocket.Remaining -= deltaTime;
                if (rocket.Remaining <= 0f)
                    ecb.DestroyEntity(entity);
            }
            ecb.Playback(state.EntityManager);
            fuseTargets.Dispose();
        }

        /// <summary>Finds the chopper this round is aimed at, then gives one evade when it flies past or a mine is left behind.</summary>
        private static void Watch(ref Rocket rocket, in NativeList<FuseTarget> targets, float3 end, in ComponentLookup<GhostOwner> owners, bool hasLog, DynamicBuffer<Session.MatchLogEntry> log, double now)
        {
            if (rocket.EvadeDone) return;
            if (rocket.Threat == Entity.Null)
            {
                for (var i = 0; i < targets.Length; i++)
                {
                    var target = targets[i];
                    if (target.Entity == rocket.Shooter || !owners.HasComponent(target.Entity)) continue;
                    if (!RocketEvade.Threatens(rocket.Position, rocket.Velocity, target.Position, rocket.ProximityFuse, rocket.SplashRadius)) continue;
                    rocket.Threat = target.Entity;
                    break;
                }
                return;
            }
            if (!TryFind(targets, rocket.Threat, out var at))
            {
                rocket.EvadeDone = true; // dead or gone
                return;
            }
            if (RocketEvade.PassedBy(rocket.Position, end, at) ||
                RocketEvade.LeftBehind(end, rocket.Velocity, at, rocket.ProximityFuse, rocket.SplashRadius))
                Evaded(ref rocket, owners, hasLog, log, now, at);
        }

        private static void Evaded(ref Rocket rocket, in ComponentLookup<GhostOwner> owners, bool hasLog, DynamicBuffer<Session.MatchLogEntry> log, double now, float3 targetAt)
        {
            if (rocket.EvadeDone) return;
            rocket.EvadeDone = true;
            if (!hasLog || !owners.TryGetComponent(rocket.Threat, out var target)) return;
            log.Add(new Session.MatchLogEntry
            {
                Kind = Session.MatchLogKind.RocketEvaded,
                Time = now,
                Actor = rocket.Attacker.NetworkId,
                Target = target.NetworkId,
                Weapon = rocket.Weapon,
                ActorPosition = rocket.Position,
                TargetPosition = targetAt,
                Distance = math.distance(rocket.Position, targetAt),
            });
        }

        private static bool TryFind(in NativeList<FuseTarget> targets, Entity entity, out float3 position)
        {
            position = default;
            if (entity == Entity.Null) return false;
            for (var i = 0; i < targets.Length; i++)
                if (targets[i].Entity == entity)
                {
                    position = targets[i].Position;
                    return true;
                }
            return false;
        }

        private struct FuseTarget
        {
            public Entity Entity;
            public float3 Position;
            public TargetClass Class;
        }

        private static bool TryFuse(in NativeList<FuseTarget> targets, in Rocket rocket, float3 end, out float3 burst)
        {
            burst = end;
            for (var i = 0; i < targets.Length; i++)
            {
                var target = targets[i];
                if (target.Entity == rocket.Shooter || (target.Class & rocket.FuseTargets) == 0) continue;
                if (RocketFlight.FuseHit(rocket.Position, end, target.Position, rocket.ProximityFuse, out burst)) return true;
            }
            return false;
        }
    }
}
