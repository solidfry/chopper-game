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
    /// Flies guided missiles: lead pursuit (<see cref="Guidance.Steer"/>) at the target once armed; a heat seeker whose
    /// target drops flares loses it and flies straight. Runs predicted on the server and on the shooter's client, so
    /// the shooter's missile moves at once. Only the server detonates: on impact, near the target (proximity fuse) or
    /// at the end of its life; then it applies damage, tells clients where it exploded, and removes the ghost.
    /// A cluster missile does not use the proximity fuse: near the target (or on its timer) the server splits it into
    /// submunition <see cref="Rocket"/>s and sends one <see cref="ClusterSplitRpc"/>.
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PhysicsSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(PylonFireSystem))]
    [UpdateBefore(typeof(ApplyDamageSystem))]
    public partial struct GuidedMissileSystem : ISystem
    {
        private ComponentLookup<LocalToWorld> _poses;
        private ComponentLookup<PhysicsVelocity> _velocities;
        private ComponentLookup<CountermeasureState> _countermeasures;
        private BufferLookup<DamageEvent> _damage;
        private ComponentLookup<GhostOwner> _owners;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PhysicsWorldSingleton>();
            _poses = state.GetComponentLookup<LocalToWorld>(isReadOnly: true);
            _velocities = state.GetComponentLookup<PhysicsVelocity>(isReadOnly: true);
            _countermeasures = state.GetComponentLookup<CountermeasureState>(isReadOnly: true);
            _damage = state.GetBufferLookup<DamageEvent>();
            _owners = state.GetComponentLookup<GhostOwner>(isReadOnly: true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var deltaTime = SystemAPI.Time.DeltaTime;
            var isServer = state.WorldUnmanaged.IsServer();
            var world = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
            _poses.Update(ref state);
            _velocities.Update(ref state);
            _countermeasures.Update(ref state);
            _damage.Update(ref state);
            _owners.Update(ref state);
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (missileRef, transform, entity) in SystemAPI.Query<RefRW<GuidedMissile>, RefRW<LocalTransform>>()
                         .WithAll<Simulate>()
                         .WithEntityAccess())
            {
                ref var missile = ref missileRef.ValueRW;
                ref var pose = ref transform.ValueRW;
                var start = pose.Position;

                missile.Remaining -= deltaTime;
                missile.ArmRemaining = math.max(0f, missile.ArmRemaining - deltaTime);

                // Flares: a heat seeker loses a target that is dropping them.
                if (missile.Seeker == SeekerType.Heat && !missile.IgnoresFlares && _countermeasures.TryGetComponent(missile.Target, out var flares) &&
                    flares.DecoyRemaining > 0f)
                    missile.Target = Entity.Null;

                LocalToWorld targetPose = default;
                var hasTarget = missile.Target != Entity.Null && _poses.TryGetComponent(missile.Target, out targetPose);
                if (hasTarget && missile.ArmRemaining <= 0f)
                {
                    var targetVelocity = _velocities.TryGetComponent(missile.Target, out var v) ? v.Linear : float3.zero;
                    missile.Velocity = Guidance.Steer(start, missile.Velocity, targetPose.Position, targetVelocity,
                        missile.Speed, missile.TurnRate, missile.NavigationGain, deltaTime);
                }

                var end = start + missile.Velocity * deltaTime;
                pose.Position = end;
                pose.Rotation = quaternion.LookRotationSafe(math.normalizesafe(missile.Velocity, math.forward()), math.up());

                // Clients only fly their copy; the server decides the explosion and removes the ghost.
                if (!isServer) continue;

                var directTarget = Entity.Null;
                var detonateAt = end;
                var detonate = false;
                var impact = Explosions.TryRaycast(world, missile.Shooter, start, end, out var hit);
                if (!impact && missile.Cluster.IsCluster)
                {
                    missile.SplitRemaining -= deltaTime;
                    var targetDistance = hasTarget ? math.distance(end, targetPose.Position) : -1f;
                    if (Cluster.ShouldSplit(missile.Cluster, math.min(missile.SplitRemaining, missile.Remaining), targetDistance))
                    {
                        Split(ecb, missile, end, _owners.TryGetComponent(entity, out var owner) ? owner.NetworkId : 0);
                        ecb.DestroyEntity(entity);
                    }
                    continue;
                }

                if (impact)
                {
                    detonate = true;
                    detonateAt = hit.Position;
                    directTarget = hit.Entity;
                }
                else if (hasTarget && math.distance(end, targetPose.Position) <= missile.ProximityFuse)
                {
                    detonate = true;
                    directTarget = missile.Target;
                }
                else if (missile.Remaining <= 0f)
                {
                    detonate = true;
                }
                if (!detonate) continue;

                if (directTarget != Entity.Null && _damage.TryGetBuffer(directTarget, out var direct))
                    direct.Add(new DamageEvent { Amount = missile.DirectDamage, Source = missile.Shooter });
                Explosions.Splash(world, ref _damage, detonateAt, missile.SplashRadius, missile.SplashDamage, missile.Shooter, directTarget);

                var rpc = ecb.CreateEntity();
                ecb.AddComponent(rpc, new MissileDetonatedRpc { Position = detonateAt });
                ecb.AddComponent<SendRpcCommandRequest>(rpc);
                ecb.DestroyEntity(entity);
            }
            ecb.Playback(state.EntityManager);
        }

        /// <summary>Server: the submunitions (straight rockets, flown and damaged by <see cref="RocketSystem"/>) and one RPC for clients.</summary>
        private static void Split(EntityCommandBuffer ecb, in GuidedMissile missile, float3 position, int shooterId)
        {
            var cluster = missile.Cluster;
            var count = math.min((int)cluster.Count, Cluster.MaxCount);
            var forward = math.normalizesafe(missile.Velocity, math.forward());
            var seed = math.hash(new float4(position, missile.Remaining));

            for (var i = 0; i < count; i++)
            {
                var rocket = ecb.CreateEntity();
                ecb.AddComponent(rocket, new Rocket
                {
                    Position = position,
                    Velocity = Cluster.Direction(forward, cluster.ConeCos, seed, i) * cluster.Speed,
                    Remaining = cluster.Lifetime,
                    Shooter = missile.Shooter,
                    DirectDamage = cluster.DirectDamage,
                    SplashDamage = cluster.SplashDamage,
                    SplashRadius = cluster.SplashRadius,
                });
            }

            var rpc = ecb.CreateEntity();
            ecb.AddComponent(rpc, new ClusterSplitRpc
            {
                Position = position,
                Forward = forward,
                Speed = cluster.Speed,
                ConeCos = cluster.ConeCos,
                Lifetime = cluster.Lifetime,
                SplashRadius = cluster.SplashRadius,
                Seed = seed,
                Count = (byte)count,
                ShooterNetworkId = shooterId,
            });
            ecb.AddComponent<SendRpcCommandRequest>(rpc);
        }
    }
}
