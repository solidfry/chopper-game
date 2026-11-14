using Chopper.Simulation.Combat;
using Chopper.Simulation.Flight;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

namespace Chopper.Simulation.Bots
{
    /// <summary>What every bot of one world shares in one update: the level, the time, the grid.</summary>
    public struct BotFrame
    {
        public CollisionWorld World;
        public float Gravity;
        public bool HasBounds;
        public ArenaBounds Bounds;
        public double Now;
        public float DeltaTime;
        /// <summary>The map's grid (<see cref="NavGrid.Blob"/> not created = no paths yet).</summary>
        public NavGrid Grid;
        /// <summary>Path searches still allowed in this update (a search is the one costly step; the server gives 1).</summary>
        public int PlansLeft;
        /// <summary>Match over: hover, no weapons, no gadgets.</summary>
        public bool Hold;
    }

    /// <summary>A live chopper, as the bots see it.</summary>
    public struct BotChopperInfo
    {
        public Entity Entity;
        /// <summary>The owner's player id (<c>GhostOwner.NetworkId</c>).</summary>
        public int OwnerId;
        public float3 Position;
        public float Health01;
    }

    /// <summary>A guided missile in flight.</summary>
    public struct BotMissileInfo
    {
        public Entity Target;
        public float3 Position;
        public float3 Velocity;
    }

    /// <summary>
    /// One bot's update, shared by the client bot (<see cref="BotPilotSystem"/>) and the server bots
    /// (<see cref="ServerBotSystem"/>):
    /// <list type="number">
    /// <item>Situation: its own chopper (health, gun, pylons, lock, flares, ability, repair), threats (missiles coming at
    /// it, locks) and the enemy choppers (distance, line of sight).</item>
    /// <item>Brain (<see cref="BotBrain"/>): mode, target, order for the pilot, goal, button presses.</item>
    /// <item>Navigator (<see cref="BotNavigation"/>, when the map's <see cref="NavGrid"/> is baked): the next waypoint
    /// to the goal; patrol goals come from <see cref="BotWander"/>.</item>
    /// <item>Eyes (<see cref="BotSensing"/>) and hands (<see cref="BotPilot"/>) → <see cref="ChopperInput"/>, the same
    /// input a player sends.</item>
    /// </list>
    /// A system keeps one of these (the component lookups), calls <see cref="Update"/> each frame, collects the world
    /// (<see cref="BotFrame"/>, choppers, missiles) and calls <see cref="Step"/> per bot.
    /// </summary>
    public struct BotFlight
    {
        /// <summary>Rockets that fall faster than this (m/s2) are bombs: bots do not aim them.</summary>
        private const float MaxBotRocketGravity = 10f;

        [ReadOnly] private ComponentLookup<LocalTransform> _transform;
        [ReadOnly] private ComponentLookup<PhysicsVelocity> _velocity;
        [ReadOnly] private ComponentLookup<ChopperFlightConfig> _flightConfig;
        [ReadOnly] private ComponentLookup<ChopperFlightState> _flightState;
        [ReadOnly] private ComponentLookup<Health> _health;
        [ReadOnly] private ComponentLookup<HitscanWeaponConfig> _gun;
        [ReadOnly] private ComponentLookup<HitscanWeaponState> _gunState;
        [ReadOnly] private ComponentLookup<PylonSelection> _pylonSelection;
        [ReadOnly] private ComponentLookup<TargetLock> _targetLock;
        [ReadOnly] private ComponentLookup<ThreatState> _threat;
        [ReadOnly] private ComponentLookup<CountermeasureConfig> _flareConfig;
        [ReadOnly] private ComponentLookup<CountermeasureState> _flares;
        [ReadOnly] private ComponentLookup<AbilityConfig> _abilityConfig;
        [ReadOnly] private ComponentLookup<AbilityState> _ability;
        [ReadOnly] private ComponentLookup<ChopperRepair> _repair;
        [ReadOnly] private BufferLookup<PylonWeapon> _pylons;
        [ReadOnly] private BufferLookup<PylonWeaponState> _pylonStates;

        public BotFlight(ref SystemState state)
        {
            _transform = state.GetComponentLookup<LocalTransform>(isReadOnly: true);
            _velocity = state.GetComponentLookup<PhysicsVelocity>(isReadOnly: true);
            _flightConfig = state.GetComponentLookup<ChopperFlightConfig>(isReadOnly: true);
            _flightState = state.GetComponentLookup<ChopperFlightState>(isReadOnly: true);
            _health = state.GetComponentLookup<Health>(isReadOnly: true);
            _gun = state.GetComponentLookup<HitscanWeaponConfig>(isReadOnly: true);
            _gunState = state.GetComponentLookup<HitscanWeaponState>(isReadOnly: true);
            _pylonSelection = state.GetComponentLookup<PylonSelection>(isReadOnly: true);
            _targetLock = state.GetComponentLookup<TargetLock>(isReadOnly: true);
            _threat = state.GetComponentLookup<ThreatState>(isReadOnly: true);
            _flareConfig = state.GetComponentLookup<CountermeasureConfig>(isReadOnly: true);
            _flares = state.GetComponentLookup<CountermeasureState>(isReadOnly: true);
            _abilityConfig = state.GetComponentLookup<AbilityConfig>(isReadOnly: true);
            _ability = state.GetComponentLookup<AbilityState>(isReadOnly: true);
            _repair = state.GetComponentLookup<ChopperRepair>(isReadOnly: true);
            _pylons = state.GetBufferLookup<PylonWeapon>(isReadOnly: true);
            _pylonStates = state.GetBufferLookup<PylonWeaponState>(isReadOnly: true);
        }

        public void Update(ref SystemState state)
        {
            _transform.Update(ref state);
            _velocity.Update(ref state);
            _flightConfig.Update(ref state);
            _flightState.Update(ref state);
            _health.Update(ref state);
            _gun.Update(ref state);
            _gunState.Update(ref state);
            _pylonSelection.Update(ref state);
            _targetLock.Update(ref state);
            _threat.Update(ref state);
            _flareConfig.Update(ref state);
            _flares.Update(ref state);
            _abilityConfig.Update(ref state);
            _ability.Update(ref state);
            _repair.Update(ref state);
            _pylons.Update(ref state);
            _pylonStates.Update(ref state);
        }

        /// <summary>True when <paramref name="chopper"/> has what a bot needs to fly it.</summary>
        public bool CanFly(Entity chopper) =>
            _transform.HasComponent(chopper) && _velocity.HasComponent(chopper) && _flightConfig.HasComponent(chopper) &&
            _flightState.HasComponent(chopper);

        /// <summary>
        /// The input of this update for the bot that flies <paramref name="chopper"/> (must pass <see cref="CanFly"/>).
        /// <paramref name="ownerId"/>: the bot's player id; choppers of the same id are not enemies.
        /// </summary>
        public ChopperInput Step(Entity chopper, int ownerId, ref BotFrame frame, in BotHandling handling,
            in NativeArray<BotChopperInfo> choppers, in NativeArray<BotMissileInfo> missiles,
            ref BotPilotState pilot, ref PilotOrder order, ref BotWander wander, ref BotBrain brain, ref BotNavigator navigator,
            DynamicBuffer<BotWaypoint> waypoints)
        {
            if (pilot.Chopper != chopper)
            {
                // New chopper (spawn or respawn): fresh hands, a new goal from here, the old fight forgotten.
                pilot = new BotPilotState { Chopper = chopper };
                wander.HasGoal = false;
                navigator.Planned = false;
                brain.Reset();
            }

            var transform = _transform[chopper];
            var velocity = _velocity[chopper].Linear;
            var flight = _flightConfig[chopper];
            var flightState = _flightState[chopper];
            var hasGrid = frame.Grid.Blob.IsCreated;

            var sense = new PilotSense
            {
                Position = transform.Position,
                Rotation = transform.Rotation,
                Velocity = velocity,
                Altitude = flightState.Altitude,
                BoostEnergy = flightState.BoostEnergy,
                Boosting = flightState.Boosting,
                Gravity = frame.Gravity,
                Obstacle = ObstacleSense.Clear,
                HasBounds = frame.HasBounds,
                BoundsCentre = frame.Bounds.Center,
                BoundsHalfExtents = frame.Bounds.HalfExtents,
            };

            var actions = default(BotActions);
            if (frame.Hold)
            {
                // Match over: stop here and wait.
                order = new PilotOrder
                {
                    Destination = sense.Position,
                    ArriveRadius = handling.ArriveRadius,
                    Speed = handling.CruiseSpeed,
                };
            }
            else
            {
                var contacts = Contacts(chopper, ownerId, sense.Position, choppers, frame.World, handling);
                var situation = Situation(chopper, transform, velocity, missiles);
                brain.Think(situation, contacts.AsArray(), handling, frame.Now, frame.DeltaTime, ref order, out var intent);
                contacts.Dispose();
                actions = intent.Actions;

                var goal = intent.Goal;
                if (intent.Wander)
                {
                    wander.Think(frame.World, chopper, sense.Position, frame.Now, sense, handling, frame.Grid, ref order);
                    goal = wander.Goal;
                }
                if (hasGrid && (intent.Wander || intent.UsePath))
                {
                    BotNavigation.Follow(ref frame.Grid.Blob.Value, ref navigator, waypoints, sense.Position, goal,
                        frame.Now, handling, NavDomain.Air, ref order, ref frame.PlansLeft);
                }
                else if (!intent.Wander)
                {
                    order.Destination = goal;
                    order.RemainingDistance = 0f;
                }
            }

            var maxAcceleration = frame.Gravity * flight.GravityScale * flight.TiltThrust * math.tan(handling.MaxTilt);
            var wanted = BotPilot.DesiredVelocity(order, sense, handling);
            sense.Obstacle = BotSensing.Probe(frame.World, chopper, sense.Position, velocity, wanted, maxAcceleration, handling);

            var input = BotPilot.Steer(order, sense, flight, handling, ref pilot.Hands, frame.DeltaTime);
            input.Repair = actions.Repair;
            input.Dive = actions.Dive;
            if (actions.Repair) input.Boost = false;
            if (actions.Ability) input.Ability.Set();
            if (actions.Countermeasure) input.Countermeasure.Set();
            if (actions.CyclePylon) input.CyclePylon.Set();
            return input;
        }

        /// <summary>Enemy choppers (all others: free for all) with distance and line of sight.</summary>
        private static NativeList<BotContact> Contacts(Entity self, int ownerId, float3 position, in NativeArray<BotChopperInfo> choppers,
            in CollisionWorld world, in BotHandling handling)
        {
            var contacts = new NativeList<BotContact>(math.max(choppers.Length, 1), Allocator.Temp);
            foreach (var other in choppers)
            {
                if (other.Entity == self || other.OwnerId == ownerId) continue;
                var distance = math.distance(position, other.Position);
                contacts.Add(new BotContact
                {
                    Entity = other.Entity,
                    Position = other.Position,
                    Health01 = other.Health01,
                    Distance = distance,
                    InSight = distance < handling.VisionRange && BotSensing.InSight(world, position, other.Position),
                });
            }
            return contacts;
        }

        private BotSituation Situation(Entity chopper, in LocalTransform transform, float3 velocity, in NativeArray<BotMissileInfo> missiles)
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

            if (_health.HasComponent(chopper))
            {
                var health = _health[chopper];
                s.Health01 = health.Max > 0f ? health.Current / health.Max : 1f;
            }

            if (_gun.HasComponent(chopper) && _gunState.HasComponent(chopper))
            {
                var gun = _gun[chopper];
                var gunState = _gunState[chopper];
                s.HasGun = gun.Range > 0f;
                s.GunRange = gun.Range;
                s.GunHeat = gunState.Heat;
                s.GunOverheated = gunState.Overheated;
            }

            if (_pylons.HasBuffer(chopper) && _pylonStates.HasBuffer(chopper))
            {
                var pylons = _pylons[chopper];
                var pylonStates = _pylonStates[chopper];
                s.SelectedPylon = _pylonSelection.HasComponent(chopper) ? _pylonSelection[chopper].Selected : 0;
                for (var i = 0; i < pylons.Length && i < pylonStates.Length; i++)
                {
                    var pylon = pylons[i];
                    var pylonState = pylonStates[i];
                    var empty = pylon.MagazineSize > 0 && pylonState.RoundsUsed >= pylon.MagazineSize;
                    if (empty) continue;
                    if (pylon.Behaviour == PylonBehaviour.Rocket)
                    {
                        // Bombs and mines need another way to aim: bots leave them alone.
                        if (s.RocketPylon >= 0 || pylon.Drag > 0f || pylon.Gravity > MaxBotRocketGravity) continue;
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

            if (_targetLock.HasComponent(chopper))
            {
                var targetLock = _targetLock[chopper];
                s.Locked = targetLock.Locked;
                s.LockTarget = targetLock.Target;
            }

            if (_threat.HasComponent(chopper))
            {
                var threat = _threat[chopper];
                s.Locking = threat.Locking;
                s.LockedBy = threat.LockedBy;
            }

            foreach (var missile in missiles)
            {
                if (missile.Target != chopper) continue;
                var offset = transform.Position - missile.Position;
                var distance = math.length(offset);
                var closing = math.dot(missile.Velocity, offset / math.max(distance, 1e-3f));
                var timeToImpact = distance / math.max(closing, 1f);
                if (timeToImpact >= s.MissileTimeToImpact) continue;
                s.MissileIncoming = true;
                s.MissileTimeToImpact = timeToImpact;
                s.MissileDirection = math.normalizesafe(missile.Velocity, offset);
            }

            if (_flareConfig.HasComponent(chopper) && _flares.HasComponent(chopper))
            {
                var config = _flareConfig[chopper];
                var flares = _flares[chopper];
                s.FlaresReady = config.Cooldown > 0f && flares.CooldownRemaining <= 0f &&
                                (config.MagazineSize == 0 || flares.RoundsUsed < config.MagazineSize);
                s.FlareUse = config.Bot;
            }

            if (_abilityConfig.HasComponent(chopper) && _ability.HasComponent(chopper))
            {
                var config = _abilityConfig[chopper];
                var ability = _ability[chopper];
                s.AbilityKind = config.Kind;
                s.AbilityReady = config.Kind != AbilityKind.None && ability.CooldownLeft <= 0f && !ability.Active;
                s.AbilityActive = ability.Active;
                s.AbilityUse = config.Bot;
                s.AbilityRadius = config.Radius;
            }

            if (_repair.HasComponent(chopper))
                s.CanRepair = _repair[chopper].Phase != (byte)RepairPhase.Locked && s.Health01 < 1f;

            return s;
        }
    }
}
