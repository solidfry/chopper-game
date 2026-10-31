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
    /// <item>Salvo (swarm): one trigger pull fires <see cref="PylonWeapon.SalvoCount"/> missiles, one per
    /// <see cref="PylonWeapon.SalvoInterval"/>, fanned out in a cone; the salvo finishes even if the trigger is let go.</item>
    /// </list>
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PhysicsSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(ExportPhysicsWorld))]
    public partial struct PylonFireSystem : ISystem
    {
        /// <summary>
        /// Client: the newest tick each pylon drew a predicted launch on (serialized <see cref="NetworkTick"/>, 0 = none).
        /// Netcode can mark a tick it already predicted as "first time" again (when it resets its last-full-tick record),
        /// so <see cref="NetworkTime.IsFirstTimeFullyPredictingTick"/> alone drew a second rocket next to the first online.
        /// A system field, so prediction rollback does not restore it.
        /// </summary>
        private FixedList64Bytes<uint> _launchTicks;

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
            var networkTime = SystemAPI.GetSingleton<NetworkTime>();
            var firstPrediction = networkTime.IsFirstTimeFullyPredictingTick;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (input, weapons, cooldownBuffer, selection, transform, velocity, owner, shooter) in SystemAPI
                         .Query<RefRO<ChopperInput>, DynamicBuffer<PylonWeapon>, DynamicBuffer<PylonWeaponState>, RefRW<PylonSelection>,
                             RefRO<LocalTransform>, RefRO<PhysicsVelocity>, RefRO<GhostOwner>>()
                         .WithAll<Simulate>()
                         .WithEntityAccess())
            {
                if (weapons.Length == 0) continue;
                // A foreach variable cannot be written through its indexer; the copy is the same buffer.
                var cooldowns = cooldownBuffer;

                var pose = transform.ValueRO;
                var shooterId = owner.ValueRO.NetworkId;
                var lockTarget = Entity.Null;
                if (SystemAPI.HasComponent<TargetLock>(shooter))
                {
                    var targetLock = SystemAPI.GetComponent<TargetLock>(shooter);
                    if (targetLock.Locked) lockTarget = targetLock.Target;
                }

                for (var i = 0; i < cooldowns.Length; i++)
                {
                    var cooldown = cooldowns[i];
                    cooldown.CooldownRemaining = math.max(0f, cooldown.CooldownRemaining - deltaTime);

                    // The rest of a salvo: one missile per interval, whatever the trigger does now.
                    if (cooldown.SalvoRemaining > 0 && i < weapons.Length)
                    {
                        cooldown.SalvoTimer -= deltaTime;
                        if (cooldown.SalvoTimer <= 0f)
                        {
                            var salvoWeapon = weapons[i];
                            cooldown.SalvoTimer += salvoWeapon.SalvoInterval;
                            var shot = salvoWeapon.SalvoCount - cooldown.SalvoRemaining;
                            cooldown.SalvoRemaining--;
                            if (isServer || (firstPrediction && ClaimLaunch(i, networkTime.ServerTick)))
                                LaunchMissile(ecb, salvoWeapon, shooter, shooterId, lockTarget, pose, velocity.ValueRO.Linear, shot);
                        }
                    }
                    cooldowns[i] = cooldown;
                }

                ref var selected = ref selection.ValueRW.Selected;
                if (input.ValueRO.CyclePylon.IsSet)
                    selected = (byte)((selected + 1) % weapons.Length);
                if (selected >= weapons.Length) selected = 0;

                if (!input.ValueRO.FireSecondary || selected >= cooldowns.Length || cooldowns[selected].CooldownRemaining > 0f) continue;

                var weapon = weapons[selected];
                if (weapon.Behaviour == PylonBehaviour.Guided && weapon.RequiresLock && lockTarget == Entity.Null) continue;

                var salvo = weapon.Behaviour == PylonBehaviour.Guided && weapon.SalvoCount > 1;
                cooldowns[selected] = new PylonWeaponState
                {
                    CooldownRemaining = weapon.FireInterval,
                    SalvoRemaining = salvo ? (byte)(weapon.SalvoCount - 1) : (byte)0,
                    SalvoTimer = salvo ? weapon.SalvoInterval : 0f,
                };

                if (weapon.Behaviour == PylonBehaviour.Rocket)
                {
                    var position = pose.Position + math.mul(pose.Rotation, weapon.MuzzleOffset);
                    var launchVelocity = velocity.ValueRO.Linear + math.mul(pose.Rotation, math.forward()) * weapon.Speed;
                    LaunchRocket(ecb, weapon, shooter, shooterId, position, launchVelocity, isServer,
                        firstPrediction && ClaimLaunch(selected, networkTime.ServerTick));
                }
                else if (isServer || (firstPrediction && ClaimLaunch(selected, networkTime.ServerTick)))
                {
                    LaunchMissile(ecb, weapon, shooter, shooterId, lockTarget, pose, velocity.ValueRO.Linear, shot: 0);
                }
            }
            ecb.Playback(state.EntityManager);
        }

        /// <summary>Client: true once per pylon and tick (see <see cref="_launchTicks"/>).</summary>
        private bool ClaimLaunch(int pylon, NetworkTick tick)
        {
            if (pylon >= _launchTicks.Capacity) return true;
            while (_launchTicks.Length <= pylon) _launchTicks.Add(0);
            var last = _launchTicks[pylon];
            if (last != 0 && !tick.IsNewerThan(new NetworkTick { SerializedData = last })) return false;
            _launchTicks[pylon] = tick.SerializedData;
            return true;
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
        /// <param name="shot">Index in the salvo (0 for a single missile): sets the spread direction and flare resistance.</param>
        private static void LaunchMissile(EntityCommandBuffer ecb, in PylonWeapon weapon, Entity shooter, int shooterId,
            Entity target, in LocalTransform pose, float3 chopperVelocity, int shot)
        {
            if (weapon.MissilePrefab == Entity.Null) return;

            var position = pose.Position + math.mul(pose.Rotation, weapon.MuzzleOffset);
            var forward = math.mul(pose.Rotation, math.forward());
            var rotation = pose.Rotation;
            if (weapon.SalvoCount > 1 && weapon.SalvoConeCos < 1f)
            {
                // Same seed for every salvo of this shooter: client and server fan out the same way.
                forward = Cluster.Direction(forward, weapon.SalvoConeCos, (uint)shooterId * 7919u, shot);
                rotation = quaternion.LookRotationSafe(forward, math.mul(pose.Rotation, math.up()));
            }
            var velocity = chopperVelocity + forward * weapon.Speed;

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
                IgnoresFlares = Salvo.IgnoresFlares(shot, weapon.FlareResistance),
            });
        }
    }
}
