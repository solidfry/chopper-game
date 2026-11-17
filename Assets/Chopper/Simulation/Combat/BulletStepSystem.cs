using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Physics.Systems;

namespace Chopper.Simulation.Combat
{
    /// <summary>
    /// Server only. Flies the nose-gun rounds (<see cref="ActiveBullet"/>) that <see cref="HitscanFireSystem"/> adds: each
    /// tick a round moves (<see cref="Ballistics.Step"/>) and ray-casts the segment it covered, in the collision world
    /// rewound by the shooter's delay at fire time (lag compensation, as the instant gun does). A hit becomes a
    /// <see cref="DamageEvent"/> (falloff by the distance flown) and removes the round; a round out of range is removed.
    /// Owns the round buffer: one entity, made here, so it exists only in the server world.
    /// The physics history must hold at least the rewind cap (0.2 s, 12 ticks at 60 Hz); Netcode keeps 16 ticks.
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PhysicsSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(HitscanFireSystem))]
    [UpdateBefore(typeof(ApplyDamageSystem))]
    public partial struct BulletStepSystem : ISystem
    {
        private BufferLookup<DamageEvent> _damageLookup;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkTime>();
            state.RequireForUpdate<PhysicsWorldSingleton>();
            var entity = state.EntityManager.CreateEntity();
            state.EntityManager.SetName(entity, "ActiveBullets");
            state.EntityManager.AddBuffer<ActiveBullet>(entity);
            _damageLookup = state.GetBufferLookup<DamageEvent>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var bullets = SystemAPI.GetSingletonBuffer<ActiveBullet>();
            if (bullets.Length == 0) return;

            var deltaTime = SystemAPI.Time.DeltaTime;
            var serverTick = SystemAPI.GetSingleton<NetworkTime>().ServerTick;
            var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().PhysicsWorld;
            var hasHistory = SystemAPI.TryGetSingleton<PhysicsWorldHistorySingleton>(out var history);
            _damageLookup.Update(ref state);

            // Backwards, so removing a round does not skip the next one.
            for (var i = bullets.Length - 1; i >= 0; i--)
            {
                var bullet = bullets[i];
                if (!Ballistics.Step(ref bullet, deltaTime, out var from))
                {
                    bullets.RemoveAt(i);
                    continue;
                }

                // Rewind from the current tick by the stored delay (never from the fire tick), so the history needs
                // only the rewind cap.
                var collisionWorld = physicsWorld.CollisionWorld;
                if (hasHistory)
                    history.GetCollisionWorldFromTick(serverTick, bullet.RewindTicks, ref physicsWorld, out collisionWorld);

                if (Explosions.TryRaycast(collisionWorld, bullet.Shooter, from, bullet.Position, out var hit))
                {
                    var flown = Ballistics.TravelledAt(bullet, hit.Position);
                    if (_damageLookup.TryGetBuffer(hit.Entity, out var damage))
                        damage.Add(new DamageEvent
                        {
                            Amount = GunDamage.At(bullet.Gun, flown),
                            Source = bullet.Shooter,
                            Point = hit.Position,
                            Normal = hit.SurfaceNormal,
                            Kind = DamageKind.Bullet,
                            Weapon = bullet.Gun.Name,
                            FlightDistance = flown,
                        });
                    bullets.RemoveAt(i);
                    continue;
                }

                if (Ballistics.Expired(bullet)) bullets.RemoveAt(i);
                else bullets[i] = bullet;
            }
        }
    }
}
