using Chopper.Simulation.Flight;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Physics.Extensions;
using Unity.Physics.Systems;
using Unity.Transforms;

namespace Chopper.Simulation.Combat
{
    /// <summary>
    /// Flies a wreck (client and server, predicted, like <see cref="ChopperFlightSystem"/>, which skips wrecks): its spin
    /// turns toward the style's spin and the rotor carries only part of its weight (<see cref="DeathStyles.Motion"/>).
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(PhysicsSystemGroup))]
    [UpdateBefore(typeof(PhysicsInitializeGroup))]
    public partial struct WreckFlightSystem : ISystem
    {
        /// <summary>How fast the spin reaches the style's spin (1/s).</summary>
        private const float SpinResponse = 3f;

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var gravity = SystemAPI.TryGetSingleton<PhysicsStep>(out var step) ? step.Gravity : PhysicsStep.Default.Gravity;
            var deltaTime = SystemAPI.Time.DeltaTime;
            foreach (var (wreck, velocity, mass, transform) in SystemAPI
                         .Query<RefRW<Wreck>, RefRW<PhysicsVelocity>, RefRO<PhysicsMass>, RefRO<LocalTransform>>()
                         .WithAll<Simulate>())
            {
                if (!wreck.ValueRO.Active) continue;
                wreck.ValueRW.Age += deltaTime;
                var rotation = transform.ValueRO.Rotation;
                var forward = math.mul(rotation, new float3(0f, 0f, 1f));
                DeathStyles.Motion(wreck.ValueRO.Style, wreck.ValueRO.Age, wreck.ValueRO.SpinSign, -forward.y,
                    out var localSpin, out var lift);

                var spin = velocity.ValueRO.GetAngularVelocityWorldSpace(mass.ValueRO, rotation);
                var target = math.mul(rotation, localSpin);
                spin = math.lerp(spin, target, 1f - math.exp(-SpinResponse * deltaTime));
                velocity.ValueRW.SetAngularVelocityWorldSpace(mass.ValueRO, rotation, spin);
                velocity.ValueRW.Linear -= gravity * (lift * deltaTime);
            }
        }
    }

    /// <summary>
    /// Server only. Turns a dead chopper into a falling wreck and explodes it (Docs/ChopperDeathDesign.md):
    /// <list type="bullet">
    /// <item>On death: the style from the killing blow, the hit zone and the height (<see cref="DeathStyles.Pick"/>); the
    /// owner is cleared, so it is nobody's chopper any more (input, HUD, camera move on); the old owner is kept for the
    /// kill cam. The kill and the respawn timer already happened at death.</item>
    /// <item>It explodes when it hits something (a velocity change over <see cref="WreckConfig.ImpactChange"/>), at once
    /// for <see cref="DeathStyle.Explode"/>, or after <see cref="WreckConfig.MaxSeconds"/>: a
    /// <see cref="WreckExplodedRpc"/> to every client, and the ghost is destroyed.</item>
    /// </list>
    /// After physics, so this tick's contacts are in the velocity.
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PhysicsSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(ExportPhysicsWorld))]
    public partial struct WreckSystem : ISystem
    {
        /// <summary>The death tick's own hit may still be in the velocity: no impact check this soon.</summary>
        private const float ImpactGrace = 0.15f;
        private const float AltitudeProbe = 1000f;

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
            var world = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            _damageLookup.Update(ref state);
            FixedString32Bytes weapon = "Wreck";

            foreach (var (wreck, health, blow, config, transform, owner, collider, entity) in SystemAPI
                         .Query<RefRW<Wreck>, RefRO<Health>, RefRO<DeathBlow>, RefRO<WreckConfig>, RefRO<LocalTransform>,
                             RefRW<GhostOwner>, RefRO<PhysicsCollider>>()
                         .WithEntityAccess())
            {
                if (!health.ValueRO.IsDead) continue;
                var pose = transform.ValueRO;

                if (!wreck.ValueRO.Active)
                {
                    var zone = Zone(collider.ValueRO, blow.ValueRO.LocalPoint);
                    var altitude = Altitude(world, entity, pose.Position);
                    wreck.ValueRW.Style = DeathStyles.Pick(config.ValueRO, blow.ValueRO, zone, altitude);
                    wreck.ValueRW.SpinSign = DeathStyles.SpinSign(zone);
                    wreck.ValueRW.OwnerNetworkId = owner.ValueRO.NetworkId;
                    wreck.ValueRW.Age = 0f;
                    owner.ValueRW.NetworkId = 0;
                }

                var velocity = SystemAPI.GetComponent<PhysicsVelocity>(entity).Linear;
                var hit = false;
                if (SystemAPI.HasComponent<CrashSensor>(entity) && wreck.ValueRO.Age > ImpactGrace)
                {
                    var sensor = SystemAPI.GetComponent<CrashSensor>(entity);
                    hit = sensor.HasVelocity && math.length(velocity - sensor.VelocityBefore) > config.ValueRO.ImpactChange;
                }
                if (wreck.ValueRO.Style != DeathStyle.Explode && !hit && wreck.ValueRO.Age < config.ValueRO.MaxSeconds) continue;
                // ChopperRespawnSystem starts the player's respawn timer, then takes the connection off the chopper.
                // It may run after this system (several physics ticks in one frame): destroying the wreck first would
                // lose the respawn for good. Wait for it (one frame at most).
                if (SystemAPI.HasComponent<Session.ConnectionOwner>(entity)) continue;

                // The blast hurts and pushes everything round it in the killer's name (a wreck that falls on a chopper
                // hurts it for the player who shot the wreck down). The wreck itself is skipped.
                var settings = config.ValueRO;
                var attacker = blow.ValueRO.Attacker;
                Explosions.Splash(world, ref _damageLookup, pose.Position, settings.BlastRadius, settings.BlastDamage,
                    Entity.Null, entity, weapon, attacker);

                // Heavy parts fly as server bodies; clients throw their biggest pieces from the same seed.
                var seed = math.hash(new int3(entity.Index, entity.Version, (int)(wreck.ValueRO.Age * 1000f))) | 1u;
                var parts = math.clamp(settings.Parts, 0, 8);
                for (var i = 0; i < parts; i++)
                {
                    WreckParts.Launch(seed, i, parts, velocity, out var offset, out var partVelocity);
                    var part = ecb.CreateEntity();
                    ecb.AddComponent(part, new WreckPart
                    {
                        Position = pose.Position + offset,
                        Velocity = partVelocity,
                        Damage = settings.PartDamage,
                        Attacker = attacker,
                    });
                }

                var rpc = ecb.CreateEntity();
                ecb.AddComponent(rpc, new WreckExplodedRpc
                {
                    Position = pose.Position,
                    Velocity = velocity,
                    Style = wreck.ValueRO.Style,
                    OwnerNetworkId = wreck.ValueRO.OwnerNetworkId,
                    PartSeed = seed,
                    Parts = (byte)parts,
                });
                ecb.AddComponent<SendRpcCommandRequest>(rpc);
                ecb.DestroyEntity(entity);
            }
            ecb.Playback(state.EntityManager);
        }

        /// <summary>The hit point as -1..1 of the collider box on each axis.</summary>
        private static float3 Zone(in PhysicsCollider collider, float3 localPoint)
        {
            if (!collider.Value.IsCreated) return float3.zero;
            var box = collider.Value.Value.CalculateAabb();
            var half = math.max(box.Extents * 0.5f, 0.01f);
            return math.clamp((localPoint - box.Center) / half, -1f, 1f);
        }

        /// <summary>Height above whatever is below (not the chopper itself); far when nothing is.</summary>
        private static float Altitude(in CollisionWorld world, Entity self, float3 position)
        {
            var hits = new NativeList<RaycastHit>(8, Allocator.Temp);
            var best = AltitudeProbe;
            var input = new RaycastInput
            {
                Start = position,
                End = position - new float3(0f, AltitudeProbe, 0f),
                Filter = CollisionFilter.Default,
            };
            if (world.CastRay(input, ref hits))
                foreach (var hit in hits)
                    if (hit.Entity != self) best = math.min(best, hit.Fraction * AltitudeProbe);
            hits.Dispose();
            return best;
        }
    }

    /// <summary>
    /// Server only. Flies the heavy wreck parts (<see cref="WreckPart"/>): gravity, a sphere sweep each tick, a bounce
    /// off the level. The first live chopper a fast part hits takes damage (and a push) in the killer's name; then the
    /// part is spent. A slow part, or one older than <see cref="WreckParts.MaxSeconds"/>, is removed.
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PhysicsSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(ExportPhysicsWorld))]
    [UpdateBefore(typeof(BlastPushSystem))]
    public partial struct WreckPartSystem : ISystem
    {
        private BufferLookup<DamageEvent> _damageLookup;
        private ComponentLookup<Health> _healthLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PhysicsWorldSingleton>();
            state.RequireForUpdate<WreckPart>();
            _damageLookup = state.GetBufferLookup<DamageEvent>();
            _healthLookup = state.GetComponentLookup<Health>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var world = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
            var gravity = SystemAPI.TryGetSingleton<PhysicsStep>(out var step) ? step.Gravity : PhysicsStep.Default.Gravity;
            var deltaTime = SystemAPI.Time.DeltaTime;
            _damageLookup.Update(ref state);
            _healthLookup.Update(ref state);
            FixedString32Bytes weapon = "Wreck part";
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (part, entity) in SystemAPI.Query<RefRW<WreckPart>>().WithEntityAccess())
            {
                ref var p = ref part.ValueRW;
                p.Age += deltaTime;
                p.Velocity += gravity * deltaTime;
                var move = p.Velocity * deltaTime;
                var distance = math.length(move);
                var speed = math.length(p.Velocity);
                if (p.Age > WreckParts.MaxSeconds || (speed < WreckParts.HarmlessSpeed && p.Age > 0.3f) || distance < 1e-5f)
                {
                    ecb.DestroyEntity(entity);
                    continue;
                }

                if (!world.SphereCast(p.Position, WreckParts.Radius, move / distance, distance, out var hit, CollisionFilter.Default))
                {
                    p.Position += move;
                    continue;
                }

                var live = _healthLookup.TryGetComponent(hit.Entity, out var health) && !health.IsDead;
                if (live && _damageLookup.TryGetBuffer(hit.Entity, out var damage))
                {
                    var amount = WreckParts.HitDamage(p.Damage, speed);
                    damage.Add(new DamageEvent
                    {
                        Amount = amount,
                        Point = hit.Position,
                        Normal = hit.SurfaceNormal,
                        Kind = DamageKind.Explosion,
                        Weapon = weapon,
                        Attacker = p.Attacker,
                        Push = p.Velocity / speed * Explosions.PushSpeed(amount),
                    });
                    ecb.DestroyEntity(entity);
                    continue;
                }

                // The level (or a wreck): stop at the surface and bounce.
                p.Position += move * hit.Fraction + hit.SurfaceNormal * 0.05f;
                p.Velocity = WreckParts.Bounce(p.Velocity, hit.SurfaceNormal);
            }
            ecb.Playback(state.EntityManager);
        }
    }
}
