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

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PhysicsWorldSingleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var world = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

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

                var rpc = ecb.CreateEntity();
                ecb.AddComponent(rpc, new WreckExplodedRpc
                {
                    Position = pose.Position,
                    Velocity = velocity,
                    Style = wreck.ValueRO.Style,
                    OwnerNetworkId = wreck.ValueRO.OwnerNetworkId,
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
}
