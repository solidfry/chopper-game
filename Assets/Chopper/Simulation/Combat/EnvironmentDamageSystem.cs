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
    /// Server only. Keeps each chopper's velocity from just before the physics step (after the flight forces), so
    /// <see cref="EnvironmentDamageSystem"/> sees only what the contacts did to it.
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PhysicsSystemGroup))]
    [UpdateAfter(typeof(PhysicsInitializeGroup))]
    [UpdateBefore(typeof(PhysicsSimulationGroup))]
    public partial struct CrashSensorSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            foreach (var (velocity, sensor) in SystemAPI.Query<RefRO<PhysicsVelocity>, RefRW<CrashSensor>>().WithAll<Simulate>())
            {
                sensor.ValueRW.VelocityBefore = velocity.ValueRO.Linear;
                sensor.ValueRW.HasVelocity = true;
            }
        }
    }

    /// <summary>
    /// Server only. Crash and out-of-bounds damage, computed from server physics (the client cannot report it).
    /// Crash: the velocity the physics step took away is the impact speed, its direction the contact normal
    /// (<see cref="CrashDamage"/>). Contacts from below are landings (own safe speed). The contact point and what was
    /// hit come from a short overlap query, so clients can show sparks or dust there. Ramming: the chopper that drove
    /// into the other gets the credit for the damage the other takes.
    /// Runs after the physics step, before <see cref="ApplyDamageSystem"/>.
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PhysicsSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(ExportPhysicsWorld))]
    [UpdateBefore(typeof(ApplyDamageSystem))]
    public partial struct EnvironmentDamageSystem : ISystem
    {
        /// <summary>Contact search reaches this far past the chopper's own size (m).</summary>
        private const float ContactMargin = 1.5f;
        /// <summary>Chopper size when it has no collider (m).</summary>
        private const float FallbackRadius = 3f;

        private ComponentLookup<CrashSensor> _sensors;
        private ComponentLookup<LocalTransform> _poses;
        private ComponentLookup<GhostOwner> _owners;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PhysicsWorldSingleton>();
            _sensors = state.GetComponentLookup<CrashSensor>(isReadOnly: true);
            _poses = state.GetComponentLookup<LocalTransform>(isReadOnly: true);
            _owners = state.GetComponentLookup<GhostOwner>(isReadOnly: true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var deltaTime = SystemAPI.Time.DeltaTime;
            var hasBounds = SystemAPI.TryGetSingleton<ArenaBounds>(out var bounds);
            var world = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
            _sensors.Update(ref state);
            _poses.Update(ref state);
            _owners.Update(ref state);

            foreach (var (velocity, config, health, transform, damage, chopper) in SystemAPI
                         .Query<RefRO<PhysicsVelocity>, RefRO<CrashDamageConfig>, RefRO<Health>, RefRO<LocalTransform>,
                             DynamicBuffer<DamageEvent>>()
                         .WithAll<Simulate, CrashSensor>()
                         .WithEntityAccess())
            {
                if (health.ValueRO.IsDead) continue;
                var position = transform.ValueRO.Position;

                var sensor = _sensors[chopper];
                if (sensor.HasVelocity)
                {
                    var speed = CrashDamage.ImpactSpeed(sensor.VelocityBefore, velocity.ValueRO.Linear, out var normal);
                    var landing = CrashDamage.IsLanding(normal);
                    var amount = CrashDamage.Damage(config.ValueRO, speed, landing, health.ValueRO.Max);
                    if (amount > 0f)
                    {
                        var radius = SystemAPI.HasComponent<PhysicsCollider>(chopper)
                            ? Radius(SystemAPI.GetComponent<PhysicsCollider>(chopper))
                            : FallbackRadius;
                        var point = Contact(world, chopper, position, normal, radius, out var other);
                        damage.Add(new DamageEvent
                        {
                            Amount = amount,
                            Source = Rammer(chopper, other, sensor.VelocityBefore, position),
                            Point = point,
                            Normal = normal,
                            Kind = landing ? DamageKind.Landing : DamageKind.Impact,
                            ImpactSpeed = speed,
                        });
                    }
                }

                if (hasBounds && !bounds.Contains(position))
                    damage.Add(new DamageEvent { Amount = bounds.DamagePerSecond * deltaTime, Source = Entity.Null, Kind = DamageKind.Bounds });
            }
        }

        /// <summary>Half the diagonal of the collider's box: a sphere round the whole chopper.</summary>
        private static float Radius(in PhysicsCollider collider)
        {
            if (!collider.Value.IsCreated) return FallbackRadius;
            var box = collider.Value.Value.CalculateAabb();
            return math.max(0.5f, math.length(box.Extents) * 0.5f);
        }

        /// <summary>
        /// The point on what the chopper hit: of the bodies near it, the one that lies most against the push
        /// (opposite the normal). With nothing found, a point on the chopper's side that faces the contact.
        /// </summary>
        private static float3 Contact(in CollisionWorld world, Entity self, float3 centre, float3 normal, float radius, out Entity other)
        {
            other = Entity.Null;
            var point = centre - normal * radius * 0.6f;
            var hits = new NativeList<DistanceHit>(8, Allocator.Temp);
            if (world.OverlapSphere(centre, radius + ContactMargin, ref hits, CollisionFilter.Default))
            {
                var best = -2f;
                foreach (var hit in hits)
                {
                    if (hit.Entity == self) continue;
                    var toward = math.dot(Explosions.SafeNormal(hit.Position - centre, -normal), -normal);
                    if (toward <= best) continue;
                    best = toward;
                    point = hit.Position;
                    other = hit.Entity;
                }
            }
            hits.Dispose();
            return point;
        }

        /// <summary>
        /// Another player's chopper that drove into this one (it closed in faster along the line between them) gets
        /// the credit; walls, the ground and a chopper that was rammed do not.
        /// </summary>
        private Entity Rammer(Entity self, Entity other, float3 selfVelocity, float3 selfPosition)
        {
            if (other == Entity.Null || other == self || !_owners.HasComponent(other)) return Entity.Null;
            if (!_sensors.TryGetComponent(other, out var otherSensor) || !_poses.TryGetComponent(other, out var otherPose))
                return Entity.Null;
            var toOther = Explosions.SafeNormal(otherPose.Position - selfPosition, new float3(0f, 0f, 1f));
            var selfClosing = math.dot(selfVelocity, toOther);
            var otherClosing = math.dot(otherSensor.VelocityBefore, -toOther);
            return otherClosing > selfClosing ? other : Entity.Null;
        }
    }
}
