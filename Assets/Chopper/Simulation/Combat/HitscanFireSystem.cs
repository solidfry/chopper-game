using Chopper.Simulation.Flight;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;

namespace Chopper.Simulation.Combat
{
    /// <summary>
    /// Hitscan with lag compensation ("favour the shooter, within limits"):
    /// <list type="bullet">
    /// <item>Client and server both run the fire rate (predicted), so shots line up with the server.</item>
    /// <item>Instant gun (<see cref="HitscanWeaponConfig.BulletSpeed"/> 0): the server ray-casts against the physics world
    /// as the shooter saw it: current tick minus the client's interpolation delay, capped at <see cref="MaxRewindSeconds"/>.</item>
    /// <item>Gun with round speed: the server adds an <see cref="ActiveBullet"/> with that same rewind delay;
    /// <see cref="BulletStepSystem"/> flies it and ray-casts each tick's segment. The name "Hitscan" stays for now.</item>
    /// <item>Hits become <see cref="DamageEvent"/>s; <see cref="ApplyDamageSystem"/> applies them.</item>
    /// </list>
    /// Lag compensation needs a <c>NetCodePhysicsConfig</c> with Lag Compensation on in the gameplay subscene.
    /// Without it the server ray-casts against the current world (no rewind).
    /// </summary>
    [BurstCompile]
    // Not in the local (menu) world: combat needs client/server worlds.
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PhysicsSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(ExportPhysicsWorld))]
    public partial struct HitscanFireSystem : ISystem
    {
        /// <summary>Players with more ping than this must lead their targets. Stops "shot behind cover".</summary>
        private const float MaxRewindSeconds = 0.2f;
        private const int DefaultTickRate = 60;

        private ComponentLookup<CommandDataInterpolationDelay> _interpolationDelayLookup;
        private BufferLookup<DamageEvent> _damageLookup;
        private ComponentLookup<PhysicsVelocity> _velocityLookup;
        private ComponentLookup<GhostOwner> _ownerLookup;
        private ComponentLookup<Session.ConnectionOwner> _connectionLookup;
        private ComponentLookup<PilotTraits> _traitsLookup;
        private ComponentLookup<AbilityConfig> _abilityConfigLookup;
        private ComponentLookup<AbilityState> _abilityLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkTime>();
            state.RequireForUpdate<PhysicsWorldSingleton>();
            _interpolationDelayLookup = state.GetComponentLookup<CommandDataInterpolationDelay>(isReadOnly: true);
            _damageLookup = state.GetBufferLookup<DamageEvent>();
            _velocityLookup = state.GetComponentLookup<PhysicsVelocity>(isReadOnly: true);
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
            var serverTick = SystemAPI.GetSingleton<NetworkTime>().ServerTick;
            var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().PhysicsWorld;
            var hasHistory = SystemAPI.TryGetSingleton<PhysicsWorldHistorySingleton>(out var history);
            var tickRate = SystemAPI.TryGetSingleton<ClientServerTickRate>(out var rates) && rates.SimulationTickRate > 0
                ? rates.SimulationTickRate
                : DefaultTickRate;
            var maxRewindTicks = (uint)math.round(MaxRewindSeconds * tickRate);

            _interpolationDelayLookup.Update(ref state);
            _damageLookup.Update(ref state);
            var now = SystemAPI.Time.ElapsedTime;
            var hasLog = SystemAPI.TryGetSingletonBuffer<Session.MatchLogEntry>(out var log) && isServer;
            // Server: the round buffer (made by BulletStepSystem, which exists only on the server).
            var hasBullets = SystemAPI.TryGetSingletonBuffer<ActiveBullet>(out var bullets) && isServer;
            _velocityLookup.Update(ref state);
            _ownerLookup.Update(ref state);
            _connectionLookup.Update(ref state);
            _traitsLookup.Update(ref state);
            _abilityConfigLookup.Update(ref state);
            _abilityLookup.Update(ref state);

            foreach (var (input, config, weapon, transform, shooter) in SystemAPI
                         .Query<RefRO<ChopperInput>, RefRO<HitscanWeaponConfig>, RefRW<HitscanWeaponState>, RefRO<LocalTransform>>()
                         .WithAll<Simulate>()
                         .WithEntityAccess())
            {
                // FireInterval 0 = this loadout has no gun.
                if (config.ValueRO.FireInterval <= 0f) continue;
                ref var weaponState = ref weapon.ValueRW;
                weaponState.CooldownRemaining = math.max(0f, weaponState.CooldownRemaining - deltaTime);
                GunHeat.Tick(ref weaponState, config.ValueRO, deltaTime);
                weaponState.Spin = GunSpin.Step(weaponState.Spin, input.ValueRO.Fire, config.ValueRO.SpinUpRate, config.ValueRO.SpinDownRate, deltaTime);
                if (!input.ValueRO.Fire || weaponState.CooldownRemaining > 0f || !GunHeat.CanFire(weaponState) ||
                    !GunSpin.Ready(weaponState.Spin)) continue;
                weaponState.CooldownRemaining = config.ValueRO.FireInterval;
                weaponState.ShotCount++;
                GunHeat.AddShot(ref weaponState, config.ValueRO);

                // Clients only predict the fire rate and shot count; presentation draws tracers from the count.
                if (!isServer) continue;
                if (hasLog)
                    log.Add(new Session.MatchLogEntry
                    {
                        Kind = Session.MatchLogKind.Shot,
                        Time = now,
                        Actor = SystemAPI.HasComponent<GhostOwner>(shooter) ? SystemAPI.GetComponent<GhostOwner>(shooter).NetworkId : 0,
                        Weapon = config.ValueRO.Name,
                        ActorPosition = transform.ValueRO.Position,
                        Distance = -1f,
                    });

                var delay = _interpolationDelayLookup.TryGetComponent(shooter, out var interpolation)
                    ? math.min(interpolation.Delay, maxRewindTicks)
                    : 0u;
                var pose = transform.ValueRO;
                var muzzle = pose.Position + math.mul(pose.Rotation, config.ValueRO.MuzzleOffset);
                var forward = math.mul(pose.Rotation, math.forward());

                // A gun with round speed: the round flies (BulletStepSystem steps it, with this rewind, from this tick on).
                if (!Ballistics.IsInstant(config.ValueRO.BulletSpeed))
                {
                    if (!hasBullets) continue;
                    var chopperVelocity = _velocityLookup.TryGetComponent(shooter, out var velocity) ? velocity.Linear : float3.zero;
                    Ballistics.AddCapped(bullets, new ActiveBullet
                    {
                        Position = muzzle,
                        Velocity = Ballistics.LaunchVelocity(chopperVelocity, forward, config.ValueRO.BulletSpeed),
                        TimeLeft = Ballistics.FlightTime(config.ValueRO.Range, config.ValueRO.BulletSpeed),
                        Shooter = shooter,
                        RewindTicks = delay,
                        Gun = config.ValueRO,
                        Attacker = AttackerSnapshot.Of(shooter, _ownerLookup, _connectionLookup, _traitsLookup, _abilityConfigLookup, _abilityLookup),
                    }, Ballistics.MaxRoundsPerShooter);
                    continue;
                }

                // Instant gun (pulse laser): one ray now.
                var collisionWorld = physicsWorld.CollisionWorld;
                // No delay (host, bots): this tick's world (see BulletStepSystem).
                if (hasHistory && delay > 0)
                    history.GetCollisionWorldFromTick(serverTick, delay, ref physicsWorld, out collisionWorld);
                if (!Explosions.TryRaycast(collisionWorld, shooter, muzzle, muzzle + forward * config.ValueRO.Range, out var hit))
                    continue;

                var distance = math.distance(muzzle, hit.Position);
                if (_damageLookup.TryGetBuffer(hit.Entity, out var damage))
                    damage.Add(new DamageEvent
                    {
                        Amount = GunDamage.At(config.ValueRO, distance),
                        Source = shooter,
                        Point = hit.Position,
                        Normal = hit.SurfaceNormal,
                        Kind = DamageKind.Bullet,
                        Weapon = config.ValueRO.Name,
                        FlightDistance = distance,
                    });
            }
        }
    }
}
