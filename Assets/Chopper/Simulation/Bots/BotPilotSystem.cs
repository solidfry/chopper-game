using Chopper.Simulation.Combat;
using Chopper.Simulation.Flight;
using Chopper.Simulation.Session;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Transforms;

namespace Chopper.Simulation.Bots
{
    /// <summary>One bot: the chopper it flies (found again after each respawn) and its hands.</summary>
    public struct BotPilotState : IComponentData
    {
        public Entity Chopper;
        public PilotHands Hands;
    }

    /// <summary>
    /// A bot flies the local chopper (client with <see cref="ScriptedPilotOverride"/>, e.g. a <c>-bot</c> build). Each frame:
    /// <list type="number">
    /// <item>Situation: its own chopper (health, gun, pylons, lock, flares, ability, repair), threats (missiles coming at
    /// it, locks) and the enemy choppers (distance, line of sight).</item>
    /// <item>Brain (<see cref="BotBrain"/>): mode, target, order for the pilot, goal, button presses.</item>
    /// <item>Navigator (<see cref="BotNavigation"/>, when the map's <see cref="NavGrid"/> is baked): the next waypoint
    /// to the goal; patrol goals come from <see cref="BotWander"/>.</item>
    /// <item>Eyes (<see cref="BotSensing"/>) and hands (<see cref="BotPilot"/>) → <see cref="ChopperInput"/>, the same
    /// input a player sends.</item>
    /// </list>
    /// Needs the <see cref="BotHandling"/> singleton (the spawner's bot profile); without it <see cref="ScriptedPilotSystem"/>
    /// flies the old fixed pattern. Server bots (filling empty slots) will run the same steps on the server.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    [UpdateBefore(typeof(CopyInputToCommandBufferSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct BotPilotSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();
            state.RequireForUpdate<ScriptedPilotOverride>();
            state.RequireForUpdate<BotHandling>();
            state.RequireForUpdate<PhysicsWorldSingleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var connection = SystemAPI.GetSingletonEntity<NetworkStreamInGame>();
            var networkId = SystemAPI.GetComponent<NetworkId>(connection).Value;
            if (SystemAPI.QueryBuilder().WithAll<BotPilotState>().Build().IsEmpty)
            {
                var seed = (uint)networkId * 7919u + 17u;
                var bot = state.EntityManager.CreateEntity();
                state.EntityManager.AddComponentData(bot, new BotPilotState());
                state.EntityManager.AddComponentData(bot, new PilotOrder());
                state.EntityManager.AddComponentData(bot, BotWander.Create(seed));
                state.EntityManager.AddComponentData(bot, BotBrain.Create(seed * 31u + 7u));
                state.EntityManager.AddComponentData(bot, new BotNavigator());
                state.EntityManager.AddBuffer<BotWaypoint>(bot);
            }

            var handling = SystemAPI.GetSingleton<BotHandling>();
            var world = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
            var gravity = math.length(SystemAPI.TryGetSingleton<PhysicsStep>(out var step) ? step.Gravity : PhysicsStep.Default.Gravity);
            var hasBounds = SystemAPI.TryGetSingleton<ArenaBounds>(out var bounds);
            var now = SystemAPI.Time.ElapsedTime;
            var deltaTime = SystemAPI.Time.DeltaTime;
            SystemAPI.TryGetSingleton<NavGrid>(out var grid);
            var hasGrid = grid.Blob.IsCreated;

            // The local chopper (one per client).
            var chopper = Entity.Null;
            foreach (var (_, entity) in SystemAPI.Query<RefRO<ChopperInput>>().WithAll<GhostOwnerIsLocal, ChopperFlightConfig>().WithEntityAccess())
                chopper = entity;
            if (chopper == Entity.Null) return;

            var transform = SystemAPI.GetComponent<LocalTransform>(chopper);
            var contacts = Contacts(ref state, chopper, networkId, transform.Position, world, handling);

            foreach (var (pilot, order, wander, brain, navigator, waypoints) in
                     SystemAPI.Query<RefRW<BotPilotState>, RefRW<PilotOrder>, RefRW<BotWander>, RefRW<BotBrain>, RefRW<BotNavigator>, DynamicBuffer<BotWaypoint>>())
            {
                if (pilot.ValueRO.Chopper != chopper)
                {
                    // New chopper (spawn or respawn): fresh hands, a new goal from here, the old fight forgotten.
                    pilot.ValueRW = new BotPilotState { Chopper = chopper };
                    wander.ValueRW.HasGoal = false;
                    navigator.ValueRW.Planned = false;
                    brain.ValueRW.Reset();
                }

                var velocity = SystemAPI.GetComponent<PhysicsVelocity>(chopper).Linear;
                var flight = SystemAPI.GetComponent<ChopperFlightConfig>(chopper);
                var flightState = SystemAPI.GetComponent<ChopperFlightState>(chopper);

                var sense = new PilotSense
                {
                    Position = transform.Position,
                    Rotation = transform.Rotation,
                    Velocity = velocity,
                    Altitude = flightState.Altitude,
                    BoostEnergy = flightState.BoostEnergy,
                    Boosting = flightState.Boosting,
                    Gravity = gravity,
                    Obstacle = ObstacleSense.Clear,
                    HasBounds = hasBounds,
                    BoundsCentre = bounds.Center,
                    BoundsHalfExtents = bounds.HalfExtents,
                };

                var situation = Situation(ref state, chopper, transform, velocity);
                brain.ValueRW.Think(situation, contacts.AsArray(), handling, now, deltaTime, ref order.ValueRW, out var intent);

                var goal = intent.Goal;
                if (intent.Wander)
                {
                    wander.ValueRW.Think(world, chopper, sense.Position, now, sense, handling, grid, ref order.ValueRW);
                    goal = wander.ValueRO.Goal;
                }
                if (hasGrid && (intent.Wander || intent.UsePath))
                {
                    BotNavigation.Follow(ref grid.Blob.Value, ref navigator.ValueRW, waypoints, sense.Position, goal,
                        now, handling, NavDomain.Air, ref order.ValueRW);
                }
                else if (!intent.Wander)
                {
                    order.ValueRW.Destination = goal;
                    order.ValueRW.RemainingDistance = 0f;
                }

                var maxAcceleration = gravity * flight.GravityScale * flight.TiltThrust * math.tan(handling.MaxTilt);
                var wanted = BotPilot.DesiredVelocity(order.ValueRO, sense, handling);
                sense.Obstacle = BotSensing.Probe(world, chopper, sense.Position, velocity, wanted, maxAcceleration, handling);

                var input = BotPilot.Steer(order.ValueRO, sense, flight, handling, ref pilot.ValueRW.Hands, deltaTime);
                var actions = intent.Actions;
                input.Repair = actions.Repair;
                input.Dive = actions.Dive;
                if (actions.Repair) input.Boost = false;
                if (actions.Ability) input.Ability.Set();
                if (actions.Countermeasure) input.Countermeasure.Set();
                if (actions.CyclePylon) input.CyclePylon.Set();
                SystemAPI.SetComponent(chopper, input);
            }
            contacts.Dispose();
        }

        /// <summary>Enemy choppers (all others: free for all) with distance and line of sight.</summary>
        private NativeList<BotContact> Contacts(ref SystemState state, Entity self, int networkId, float3 position,
            in CollisionWorld world, in BotHandling handling)
        {
            var contacts = new NativeList<BotContact>(8, Allocator.Temp);
            foreach (var (pose, health, owner, entity) in
                     SystemAPI.Query<RefRO<LocalTransform>, RefRO<Health>, RefRO<GhostOwner>>().WithAll<ChopperFlightConfig>().WithEntityAccess())
            {
                if (entity == self || owner.ValueRO.NetworkId == networkId || health.ValueRO.Current <= 0f) continue;
                var distance = math.distance(position, pose.ValueRO.Position);
                contacts.Add(new BotContact
                {
                    Entity = entity,
                    Position = pose.ValueRO.Position,
                    Health01 = health.ValueRO.Max > 0f ? health.ValueRO.Current / health.ValueRO.Max : 1f,
                    Distance = distance,
                    InSight = distance < handling.VisionRange && BotSensing.InSight(world, position, pose.ValueRO.Position),
                });
            }
            return contacts;
        }

        private BotSituation Situation(ref SystemState state, Entity chopper, in LocalTransform transform, float3 velocity)
        {
            var s = new BotSituation
            {
                Self = chopper,
                Position = transform.Position,
                Velocity = velocity,
                Forward = math.mul(transform.Rotation, math.forward()),
                Health01 = 1f,
                RocketPylon = -1,
                SeekerPylon = -1,
                MissileTimeToImpact = float.MaxValue,
            };

            if (SystemAPI.HasComponent<Health>(chopper))
            {
                var health = SystemAPI.GetComponent<Health>(chopper);
                s.Health01 = health.Max > 0f ? health.Current / health.Max : 1f;
            }

            if (SystemAPI.HasComponent<HitscanWeaponConfig>(chopper) && SystemAPI.HasComponent<HitscanWeaponState>(chopper))
            {
                var gun = SystemAPI.GetComponent<HitscanWeaponConfig>(chopper);
                var gunState = SystemAPI.GetComponent<HitscanWeaponState>(chopper);
                s.HasGun = gun.Range > 0f;
                s.GunRange = gun.Range;
                s.GunHeat = gunState.Heat;
                s.GunOverheated = gunState.Overheated;
            }

            if (SystemAPI.HasBuffer<PylonWeapon>(chopper) && SystemAPI.HasBuffer<PylonWeaponState>(chopper))
            {
                var pylons = SystemAPI.GetBuffer<PylonWeapon>(chopper);
                var pylonStates = SystemAPI.GetBuffer<PylonWeaponState>(chopper);
                s.SelectedPylon = SystemAPI.HasComponent<PylonSelection>(chopper) ? SystemAPI.GetComponent<PylonSelection>(chopper).Selected : 0;
                for (var i = 0; i < pylons.Length && i < pylonStates.Length; i++)
                {
                    var pylon = pylons[i];
                    var pylonState = pylonStates[i];
                    var empty = pylon.MagazineSize > 0 && pylonState.RoundsUsed >= pylon.MagazineSize;
                    if (empty) continue;
                    if (pylon.Behaviour == PylonBehaviour.Rocket && s.RocketPylon < 0)
                    {
                        s.RocketPylon = i;
                        s.RocketSpeed = pylon.Speed;
                    }
                    else if (pylon.Behaviour == PylonBehaviour.Guided && pylon.Seeker != SeekerType.None &&
                             (pylon.Targets & TargetClass.Air) != 0 && s.SeekerPylon < 0)
                    {
                        s.SeekerPylon = i;
                        s.SeekerLockRange = pylon.LockRange;
                    }
                }
            }

            if (SystemAPI.HasComponent<TargetLock>(chopper))
            {
                var targetLock = SystemAPI.GetComponent<TargetLock>(chopper);
                s.Locked = targetLock.Locked;
                s.LockTarget = targetLock.Target;
            }

            if (SystemAPI.HasComponent<ThreatState>(chopper))
            {
                var threat = SystemAPI.GetComponent<ThreatState>(chopper);
                s.Locking = threat.Locking;
                s.LockedBy = threat.LockedBy;
            }

            foreach (var (missile, pose) in SystemAPI.Query<RefRO<GuidedMissile>, RefRO<LocalTransform>>())
            {
                if (missile.ValueRO.Target != chopper) continue;
                var offset = transform.Position - pose.ValueRO.Position;
                var distance = math.length(offset);
                var closing = math.dot(missile.ValueRO.Velocity, offset / math.max(distance, 1e-3f));
                var timeToImpact = distance / math.max(closing, 1f);
                if (timeToImpact >= s.MissileTimeToImpact) continue;
                s.MissileIncoming = true;
                s.MissileTimeToImpact = timeToImpact;
                s.MissileDirection = math.normalizesafe(missile.ValueRO.Velocity, offset);
            }

            if (SystemAPI.HasComponent<CountermeasureConfig>(chopper) && SystemAPI.HasComponent<CountermeasureState>(chopper))
            {
                var config = SystemAPI.GetComponent<CountermeasureConfig>(chopper);
                var flares = SystemAPI.GetComponent<CountermeasureState>(chopper);
                s.FlaresReady = config.Cooldown > 0f && flares.CooldownRemaining <= 0f &&
                                (config.MagazineSize == 0 || flares.RoundsUsed < config.MagazineSize);
                s.FlareUse = config.Bot;
            }

            if (SystemAPI.HasComponent<AbilityConfig>(chopper) && SystemAPI.HasComponent<AbilityState>(chopper))
            {
                var config = SystemAPI.GetComponent<AbilityConfig>(chopper);
                var ability = SystemAPI.GetComponent<AbilityState>(chopper);
                s.AbilityKind = config.Kind;
                s.AbilityReady = config.Kind != AbilityKind.None && ability.CooldownLeft <= 0f && !ability.Active;
                s.AbilityActive = ability.Active;
                s.AbilityUse = config.Bot;
                s.AbilityRadius = config.Radius;
            }

            if (SystemAPI.HasComponent<ChopperRepair>(chopper))
                s.CanRepair = SystemAPI.GetComponent<ChopperRepair>(chopper).Phase != (byte)RepairPhase.Locked && s.Health01 < 1f;

            return s;
        }
    }
}
