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
    /// Launches rockets. Client and server both run the fire rate (predicted). The server creates the real
    /// <see cref="Rocket"/> and tells everyone; the owner's client draws its own rocket at once.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(PredictedFixedStepSimulationSystemGroup))]
    [UpdateAfter(typeof(PhysicsSystemGroup))]
    public partial struct RocketFireSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkTime>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var deltaTime = SystemAPI.Time.DeltaTime;
            var isServer = state.WorldUnmanaged.IsServer();
            // Prediction replays old ticks; only the first prediction of a tick may make a visual.
            var firstPrediction = SystemAPI.GetSingleton<NetworkTime>().IsFirstTimeFullyPredictingTick;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (input, config, launcher, transform, velocity, owner, shooter) in SystemAPI
                         .Query<RefRO<ChopperInput>, RefRO<RocketLauncherConfig>, RefRW<RocketLauncherState>,
                             RefRO<LocalTransform>, RefRO<PhysicsVelocity>, RefRO<GhostOwner>>()
                         .WithAll<Simulate>()
                         .WithEntityAccess())
            {
                ref var launcherState = ref launcher.ValueRW;
                launcherState.CooldownRemaining = math.max(0f, launcherState.CooldownRemaining - deltaTime);
                if (!input.ValueRO.FireSecondary || launcherState.CooldownRemaining > 0f) continue;
                launcherState.CooldownRemaining = config.ValueRO.FireInterval;

                var rockets = config.ValueRO;
                var pose = transform.ValueRO;
                var position = pose.Position + math.mul(pose.Rotation, rockets.MuzzleOffset);
                var rocketVelocity = velocity.ValueRO.Linear + math.mul(pose.Rotation, math.forward()) * rockets.Speed;

                if (isServer)
                {
                    var rocket = ecb.CreateEntity();
                    ecb.AddComponent(rocket, new Rocket
                    {
                        Position = position,
                        Velocity = rocketVelocity,
                        Remaining = rockets.Lifetime,
                        Shooter = shooter,
                        DirectDamage = rockets.DirectDamage,
                        SplashDamage = rockets.SplashDamage,
                        SplashRadius = rockets.SplashRadius,
                    });

                    var rpc = ecb.CreateEntity();
                    ecb.AddComponent(rpc, new RocketLaunchRpc
                    {
                        Position = position,
                        Velocity = rocketVelocity,
                        Lifetime = rockets.Lifetime,
                        SplashRadius = rockets.SplashRadius,
                        ShooterNetworkId = owner.ValueRO.NetworkId,
                    });
                    ecb.AddComponent<SendRpcCommandRequest>(rpc); // No target = every connection.
                }
                else if (firstPrediction)
                {
                    var launch = ecb.CreateEntity();
                    ecb.AddComponent(launch, new LocalRocketLaunch
                    {
                        Position = position,
                        Velocity = rocketVelocity,
                        Lifetime = rockets.Lifetime,
                        SplashRadius = rockets.SplashRadius,
                    });
                }
            }
            ecb.Playback(state.EntityManager);
        }
    }
}
