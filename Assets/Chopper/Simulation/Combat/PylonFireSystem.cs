using Chopper.Simulation.Flight;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;
using Chopper.Simulation.Session;

namespace Chopper.Simulation.Combat
{
    /// <summary>
    /// Secondary fire: the selected pylon weapon (cycled with input). Client and server both run cooldowns and the
    /// selection (predicted).
    /// <list type="bullet">
    /// <item>Rocket: the server creates the real <see cref="Rocket"/> and tells everyone; the owner draws its own at once.</item>
    /// <item>Guided: the server spawns the missile ghost; the owner spawns a predicted copy that the server ghost then
    /// takes over. Weapons with <see cref="PylonWeapon.RequiresLock"/> do not fire without a lock.</item>
    /// <item>Salvo (swarm, salvo pod): one trigger pull fires <see cref="PylonWeapon.SalvoCount"/> missiles or rockets,
    /// one per <see cref="PylonWeapon.SalvoInterval"/>, fanned out in a cone; the salvo finishes even if the trigger is let go.</item>
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
        private ComponentLookup<GhostOwner> _ownerLookup;
        private ComponentLookup<Session.ConnectionOwner> _connectionLookup;
        private ComponentLookup<PilotTraits> _traitsLookup;
        private ComponentLookup<AbilityConfig> _abilityConfigLookup;
        private ComponentLookup<AbilityState> _abilityLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkTime>();
            _ownerLookup = state.GetComponentLookup<GhostOwner>(isReadOnly: true);
            _connectionLookup = state.GetComponentLookup<Session.ConnectionOwner>(isReadOnly: true);
            _traitsLookup = state.GetComponentLookup<PilotTraits>(isReadOnly: true);
            _abilityConfigLookup = state.GetComponentLookup<AbilityConfig>(isReadOnly: true);
            _abilityLookup = state.GetComponentLookup<AbilityState>(isReadOnly: true);
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
            // Team modes: live team mates the repair drone can fly to (empty in Free For All).
            var teamChoppers = new NativeList<DroneAlly>(8, Allocator.Temp);
            foreach (var (team, health, chopperPose, chopper) in SystemAPI.Query<RefRO<Match.Team>, RefRO<Health>, RefRO<LocalTransform>>().WithEntityAccess())
            {
                if (team.ValueRO.Value == Match.TeamRules.None || health.ValueRO.IsDead) continue;
                teamChoppers.Add(new DroneAlly
                {
                    Entity = chopper,
                    Position = chopperPose.ValueRO.Position,
                    Hurt = health.ValueRO.Current < health.ValueRO.Max,
                    Team = team.ValueRO.Value,
                });
            }
            var now = SystemAPI.Time.ElapsedTime;
            // Server: one Shot per rocket or missile for the match log (when logging is on).
            var hasLog = SystemAPI.TryGetSingletonBuffer<MatchLogEntry>(out var log) && isServer;
            _ownerLookup.Update(ref state);
            _connectionLookup.Update(ref state);
            _traitsLookup.Update(ref state);
            _abilityConfigLookup.Update(ref state);
            _abilityLookup.Update(ref state);

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
                // Server: who fired, kept on the rocket or missile for kill credit if this chopper dies first.
                var attacker = isServer ? AttackerSnapshot.Of(shooter, _ownerLookup, _connectionLookup, _traitsLookup, _abilityConfigLookup, _abilityLookup) : default;
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
                    Magazine.Tick(ref cooldown.RoundsUsed, ref cooldown.ReloadRemaining, deltaTime);

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
                            var claimed = firstPrediction && ClaimLaunch(i, networkTime.ServerTick);
                            if (salvoWeapon.Behaviour == PylonBehaviour.Rocket)
                                LaunchRocket(ecb, salvoWeapon, shooter, shooterId, attacker, pose, velocity.ValueRO.Linear, shot, isServer, claimed);
                            else if (isServer || claimed)
                                LaunchMissile(ecb, salvoWeapon, shooter, shooterId, attacker, lockTarget, pose, velocity.ValueRO.Linear, shot);
                            if (hasLog) LogShot(log, now, shooterId, salvoWeapon.Name, pose.Position);
                        }
                    }
                    cooldowns[i] = cooldown;
                }

                ref var selected = ref selection.ValueRW.Selected;
                if (input.ValueRO.CyclePylon.IsSet)
                    selected = (byte)((selected + 1) % weapons.Length);
                // Keys 1-5: a slot this loadout does not have changes nothing.
                var picked = input.ValueRO.SelectPylon;
                if (picked > 0 && picked <= weapons.Length) selected = (byte)(picked - 1);
                if (selected >= weapons.Length) selected = 0;

                if (!input.ValueRO.FireSecondary || selected >= cooldowns.Length || cooldowns[selected].CooldownRemaining > 0f) continue;
                // Carrying the nuke: no pylon weapons (plan section 7).
                if (SystemAPI.HasComponent<Match.NukeCarry>(shooter) && SystemAPI.GetComponent<Match.NukeCarry>(shooter).Carrying) continue;

                var weapon = weapons[selected];
                if (weapon.Behaviour == PylonBehaviour.Guided && weapon.RequiresLock && lockTarget == Entity.Null) continue;
                // Drone: only when it is docked and its target is hurt (never wasted on a full hull).
                var droneTarget = weapon.Behaviour == PylonBehaviour.Drone ? DroneTarget(ref state, shooter, pose, weapon, teamChoppers) : shooter;
                if (weapon.Behaviour == PylonBehaviour.Drone &&
                    (!SystemAPI.HasComponent<RepairDroneState>(shooter) || !SystemAPI.HasComponent<Health>(droneTarget) ||
                     !RepairDrone.CanLaunch(SystemAPI.GetComponent<RepairDroneState>(shooter), SystemAPI.GetComponent<Health>(droneTarget))))
                    continue;
                var fired = cooldowns[selected];
                if (!Magazine.HasRound(fired.RoundsUsed, weapon.MagazineSize)) continue;

                var salvo = weapon.SalvoCount > 1;
                fired.CooldownRemaining = weapon.FireInterval;
                fired.SalvoRemaining = salvo ? (byte)(weapon.SalvoCount - 1) : (byte)0;
                fired.SalvoTimer = salvo ? weapon.SalvoInterval : 0f;
                Magazine.Spend(ref fired.RoundsUsed, ref fired.ReloadRemaining, weapon.MagazineSize, weapon.ReloadSeconds);
                cooldowns[selected] = fired;

                if (weapon.Behaviour == PylonBehaviour.Rocket)
                {
                    LaunchRocket(ecb, weapon, shooter, shooterId, attacker, pose, velocity.ValueRO.Linear, shot: 0, isServer,
                        firstPrediction && ClaimLaunch(selected, networkTime.ServerTick));
                }
                else if (weapon.Behaviour == PylonBehaviour.Drone)
                {
                    // Server only: the drone state is sent to everyone, so clients draw it from there.
                    if (isServer)
                    {
                        var drone = SystemAPI.GetComponent<RepairDroneState>(shooter);
                        var distance = SystemAPI.HasComponent<LocalTransform>(droneTarget)
                            ? math.distance(pose.Position, SystemAPI.GetComponent<LocalTransform>(droneTarget).Position)
                            : 0f;
                        RepairDrone.Launch(ref drone, droneTarget, distance, weapon.Speed, weapon.HealShare, weapon.HealSeconds,
                            SystemAPI.GetComponent<Health>(droneTarget).Max);
                        ecb.SetComponent(shooter, drone);
                    }
                    continue;
                }
                else if (isServer || (firstPrediction && ClaimLaunch(selected, networkTime.ServerTick)))
                {
                    LaunchMissile(ecb, weapon, shooter, shooterId, attacker, lockTarget, pose, velocity.ValueRO.Linear, shot: 0);
                }
                if (hasLog) LogShot(log, now, shooterId, weapon.Name, pose.Position);
            }
            teamChoppers.Dispose();
            ecb.Playback(state.EntityManager);
        }

        private static void LogShot(DynamicBuffer<MatchLogEntry> log, double now, int shooterId, in FixedString32Bytes weapon, float3 position) =>
            log.Add(new MatchLogEntry
            {
                Kind = MatchLogKind.Shot,
                Time = now,
                Actor = shooterId,
                Weapon = weapon,
                ActorPosition = position,
                Distance = -1f,
            });

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

        /// <param name="shot">Index in the salvo (0 for a single rocket): sets the spread direction.</param>
        private static void LaunchRocket(EntityCommandBuffer ecb, in PylonWeapon weapon, Entity shooter, int shooterId, in AttackerSnapshot attacker,
            in LocalTransform pose, float3 chopperVelocity, int shot, bool isServer, bool firstPrediction)
        {
            var position = pose.Position + math.mul(pose.Rotation, weapon.MuzzleOffset);
            var forward = RocketFlight.SalvoDirection(math.mul(pose.Rotation, math.forward()), weapon.SalvoCount, weapon.SalvoConeCos,
                shooterId, shot);
            var velocity = chopperVelocity + forward * weapon.Speed;
            if (isServer)
            {
                var rocket = ecb.CreateEntity();
                ecb.AddComponent(rocket, new Rocket
                {
                    Position = position,
                    Velocity = velocity,
                    Remaining = weapon.Lifetime,
                    Shooter = shooter,
                    Attacker = attacker,
                    DirectDamage = weapon.DirectDamage,
                    SplashDamage = weapon.SplashDamage,
                    SplashRadius = weapon.SplashRadius,
                    Gravity = weapon.Gravity,
                    Drag = weapon.Drag,
                    ProximityFuse = weapon.ProximityFuse,
                    FuseTargets = weapon.Targets,
                    Weapon = weapon.Name,
                });

                var rpc = ecb.CreateEntity();
                ecb.AddComponent(rpc, new RocketLaunchRpc
                {
                    Position = position,
                    Velocity = velocity,
                    Lifetime = weapon.Lifetime,
                    SplashRadius = weapon.SplashRadius,
                    Gravity = weapon.Gravity,
                    Drag = weapon.Drag,
                    ProximityFuse = weapon.ProximityFuse,
                    FuseTargets = (byte)weapon.Targets,
                    ShooterNetworkId = shooterId,
                    ModuleId = weapon.ModuleId,
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
                    Gravity = weapon.Gravity,
                    Drag = weapon.Drag,
                    ProximityFuse = weapon.ProximityFuse,
                    FuseTargets = weapon.Targets,
                    ModuleId = weapon.ModuleId,
                });
            }
        }

        /// <summary>
        /// Same code on server (real ghost) and owner client (predicted spawn: Netcode matches it to the server ghost
        /// by type and spawn tick; if the server did not fire, the predicted copy is removed).
        /// </summary>
        /// <param name="shot">Index in the salvo (0 for a single missile): sets the spread direction and flare resistance.</param>
        private static void LaunchMissile(EntityCommandBuffer ecb, in PylonWeapon weapon, Entity shooter, int shooterId, in AttackerSnapshot attacker,
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
            // Curving flight: full speed is what the old straight launch had; the missile leaves at part of it, kicked up
            // and out to the pylon side, and the flight system speeds it up and bends it round (MissileArc).
            var fullSpeed = math.length(chopperVelocity + forward * weapon.Speed);
            var arc = weapon.Arc;
            var kicked = MissileArcMath.KickDirection(forward, math.mul(pose.Rotation, math.up()), math.mul(pose.Rotation, math.right()),
                weapon.MuzzleOffset.x, arc.KickRadians, shot);
            if (arc.KickRadians > 0f) rotation = quaternion.LookRotationSafe(kicked, math.mul(pose.Rotation, math.up()));
            var velocity = arc.KickRadians > 0f || arc.BoostSeconds > 0f
                ? kicked * MissileArcMath.SpeedAt(arc, fullSpeed, 0f)
                : chopperVelocity + forward * weapon.Speed;

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
                Attacker = attacker,
                ProximityFuse = weapon.ProximityFuse,
                DirectDamage = weapon.DirectDamage,
                SplashDamage = weapon.SplashDamage,
                SplashRadius = weapon.SplashRadius,
                Cluster = weapon.Cluster,
                SplitRemaining = weapon.Cluster.SplitTime,
                IgnoresFlares = Salvo.IgnoresFlares(shot, weapon.FlareResistance),
                Weapon = weapon.Name,
                Arc = arc,
                Lifetime = weapon.Lifetime,
                FullSpeed = fullSpeed,
                WeavePhase = MissileArcMath.Phase(shooterId, shot),
            });
        }

        /// <summary>The drone's target: a hurt team mate under the reticle (team modes), else the shooter.</summary>
        private Entity DroneTarget(ref SystemState state, Entity shooter, in LocalTransform pose, in PylonWeapon weapon,
            NativeList<DroneAlly> teamChoppers)
        {
            var own = SystemAPI.HasComponent<Match.Team>(shooter) ? SystemAPI.GetComponent<Match.Team>(shooter).Value : Match.TeamRules.None;
            if (own == Match.TeamRules.None || teamChoppers.Length == 0) return shooter;
            var allies = new NativeList<DroneAlly>(teamChoppers.Length, Allocator.Temp);
            foreach (var entry in teamChoppers)
                if (Match.TeamRules.AreAllies(own, entry.Team)) allies.Add(entry);
            var forward = math.mul(pose.Rotation, math.forward());
            var range = weapon.LockRange > 0f ? weapon.LockRange : RepairDrone.DefaultAimRange;
            var cone = weapon.LockConeCos > 0f ? weapon.LockConeCos : RepairDrone.DefaultAimConeCos;
            var target = RepairDrone.PickTarget(shooter, pose.Position, forward, allies.AsArray(), range, cone);
            allies.Dispose();
            return target;
        }
    }
}
