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
    /// Secondary fire: the selected pylon weapon (cycled with input). Client and server both run cooldowns and the
    /// selection (predicted).
    /// <list type="bullet">
    /// <item>Rocket: the server creates the real <see cref="Rocket"/> and tells everyone; the owner draws its own at once.</item>
    /// <item>Guided: the server spawns the missile ghost; the owner spawns a predicted copy that the server ghost then
    /// takes over. Weapons with <see cref="PylonWeapon.RequiresLock"/> do not fire without a lock.</item>
    /// </list>
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PhysicsSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(ExportPhysicsWorld))]
    public partial struct PylonFireSystem : ISystem
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
            // Prediction replays old ticks; only the first prediction of a tick may spawn things on the client.
            var firstPrediction = SystemAPI.GetSingleton<NetworkTime>().IsFirstTimeFullyPredictingTick;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (input, weapons, cooldowns, selection, transform, velocity, owner, shooter) in SystemAPI
                         .Query<RefRO<ChopperInput>, DynamicBuffer<PylonWeapon>, DynamicBuffer<PylonWeaponState>, RefRW<PylonSelection>,
                             RefRO<LocalTransform>, RefRO<PhysicsVelocity>, RefRO<GhostOwner>>()
                         .WithAll<Simulate>()
                         .WithEntityAccess())
            {
                if (weapons.Length == 0) continue;

                for (var i = 0; i < cooldowns.Length; i++)
                {
                    var cooldown = cooldowns[i];
                    cooldown.CooldownRemaining = math.max(0f, cooldown.CooldownRemaining - deltaTime);
                    cooldowns[i] = cooldown;
                }

                ref var selected = ref selection.ValueRW.Selected;
                if (input.ValueRO.CyclePylon.IsSet)
                    selected = (byte)((selected + 1) % weapons.Length);
                if (selected >= weapons.Length) selected = 0;

                if (!input.ValueRO.FireSecondary || selected >= cooldowns.Length || cooldowns[selected].CooldownRemaining > 0f) continue;

                var weapon = weapons[selected];
                var hasLock = false;
                var lockTarget = Entity.Null;
                if (SystemAPI.HasComponent<TargetLock>(shooter))
                {
                    var targetLock = SystemAPI.GetComponent<TargetLock>(shooter);
                    hasLock = targetLock.Locked && targetLock.Target != Entity.Null;
                    lockTarget = hasLock ? targetLock.Target : Entity.Null;
                }
                if (weapon.Behaviour == PylonBehaviour.Guided && weapon.RequiresLock && !hasLock) continue;

                cooldowns[selected] = new PylonWeaponState { CooldownRemaining = weapon.FireInterval };

                var pose = transform.ValueRO;
                var position = pose.Position + math.mul(pose.Rotation, weapon.MuzzleOffset);
                var launchVelocity = velocity.ValueRO.Linear + math.mul(pose.Rotation, math.forward()) * weapon.Speed;

                if (weapon.Behaviour == PylonBehaviour.Rocket)
                    LaunchRocket(ecb, weapon, shooter, owner.ValueRO.NetworkId, position, launchVelocity, isServer, firstPrediction);
                else if (isServer || firstPrediction)
                    LaunchMissile(ecb, weapon, shooter, owner.ValueRO.NetworkId, lockTarget, position, pose.Rotation, launchVelocity);
            }
            ecb.Playback(state.EntityManager);
        }

        private static void LaunchRocket(EntityCommandBuffer ecb, in PylonWeapon weapon, Entity shooter, int shooterId,
            float3 position, float3 velocity, bool isServer, bool firstPrediction)
        {
            if (isServer)
            {
                var rocket = ecb.CreateEntity();
                ecb.AddComponent(rocket, new Rocket
                {
                    Position = position,
                    Velocity = velocity,
                    Remaining = weapon.Lifetime,
                    Shooter = shooter,
                    DirectDamage = weapon.DirectDamage,
                    SplashDamage = weapon.SplashDamage,
                    SplashRadius = weapon.SplashRadius,
                });

                var rpc = ecb.CreateEntity();
                ecb.AddComponent(rpc, new RocketLaunchRpc
                {
                    Position = position,
                    Velocity = velocity,
                    Lifetime = weapon.Lifetime,
                    SplashRadius = weapon.SplashRadius,
                    ShooterNetworkId = shooterId,
                });
                ecb.AddComponent<SendRpcCommandRequest>(rpc); // No target = every connection.
            }
            else if (firstPrediction)
            {
                var launch = ecb.CreateEntity();
                ecb.AddComponent(launch, new LocalRocketLaunch
                {
                    Position = position,
                    Velocity = velocity,
                    Lifetime = weapon.Lifetime,
                    SplashRadius = weapon.SplashRadius,
                });
            }
        }

        /// <summary>
        /// Same code on server (real ghost) and owner client (predicted spawn: Netcode matches it to the server ghost
        /// by type and spawn tick; if the server did not fire, the predicted copy is removed).
        /// </summary>
        private static void LaunchMissile(EntityCommandBuffer ecb, in PylonWeapon weapon, Entity shooter, int shooterId,
            Entity target, float3 position, quaternion rotation, float3 velocity)
        {
            if (weapon.MissilePrefab == Entity.Null) return;

            var missile = ecb.Instantiate(weapon.MissilePrefab);
            ecb.SetComponent(missile, LocalTransform.FromPositionRotation(position, rotation));
            ecb.SetComponent(missile, new GhostOwner { NetworkId = shooterId });
            ecb.SetComponent(missile, new GuidedMissile
            {
                Target = target,
                Velocity = velocity,
                Speed = math.length(velocity),
                TurnRate = weapon.TurnRate,
                NavigationGain = weapon.NavigationGain,
                Remaining = weapon.Lifetime,
                ArmRemaining = weapon.ArmTime,
                Seeker = weapon.Seeker,
                Shooter = shooter,
                ProximityFuse = weapon.ProximityFuse,
                DirectDamage = weapon.DirectDamage,
                SplashDamage = weapon.SplashDamage,
                SplashRadius = weapon.SplashRadius,
                Cluster = weapon.Cluster,
                SplitRemaining = weapon.Cluster.SplitTime,
            });
        }
    }
}
