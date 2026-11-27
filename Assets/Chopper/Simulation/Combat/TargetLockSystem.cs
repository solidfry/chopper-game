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
    /// Server only. Builds the lock of each chopper's selected seeker weapon: the best <see cref="Targetable"/> in the
    /// cone (hot, centred, close) with a clear line of sight. Keeping the same target fills the lock over the weapon's
    /// lock time; a target with flares out cannot be locked. A target being locked is kept while it stays in the cone,
    /// even if another scores higher. When it leaves the cone or line of sight the lock holds for the weapon's grace time
    /// (progress frozen) and breaks after that. Flares, death or range break it at once.
    /// Also fills each target's <see cref="ThreatState"/>.
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PhysicsSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(ExportPhysicsWorld))]
    [UpdateAfter(typeof(CountermeasureSystem))]
    [UpdateAfter(typeof(PilotAbilitySystem))]
    [UpdateBefore(typeof(PylonFireSystem))]
    public partial struct TargetLockSystem : ISystem
    {
        private struct Candidate
        {
            public Entity Entity;
            public float3 Position;
            public TargetClass Class;
            public float Heat;
            public float LockScale;
            /// <summary>The flares of <see cref="Entity"/>, not the chopper.</summary>
            public bool Flares;
            /// <summary><see cref="Match.Team"/> of the target (0 = none); team mates are never locked.</summary>
            public byte Team;
        }

        private ComponentLookup<ThreatState> _threats;
        private ComponentLookup<AbilityState> _abilities;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PhysicsWorldSingleton>();
            _threats = state.GetComponentLookup<ThreatState>();
            _abilities = state.GetComponentLookup<AbilityState>(isReadOnly: true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var deltaTime = SystemAPI.Time.DeltaTime;
            var world = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
            _threats.Update(ref state);
            _abilities.Update(ref state);

            var candidates = CollectCandidates(ref state);

            foreach (var threat in SystemAPI.Query<RefRW<ThreatState>>())
                threat.ValueRW = default;

            foreach (var (weapons, selection, transform, targetLock, self) in SystemAPI
                         .Query<DynamicBuffer<PylonWeapon>, RefRO<PylonSelection>, RefRO<LocalTransform>, RefRW<TargetLock>>()
                         .WithEntityAccess())
            {
                ref var lockState = ref targetLock.ValueRW;
                var selected = selection.ValueRO.Selected;
                // Hit by a Blackout (pilot HEX): no lock until it wears off.
                var jammed = _abilities.TryGetComponent(self, out var ability) && ability.Jammed;
                if (jammed || selected >= weapons.Length || weapons[selected].Seeker == SeekerType.None)
                {
                    lockState = default;
                    continue;
                }

                var weapon = weapons[selected];
                var pose = transform.ValueRO;
                var selfTeam = TeamOf(ref state, self);
                var origin = pose.Position + math.mul(pose.Rotation, weapon.MuzzleOffset);
                var forward = math.mul(pose.Rotation, math.forward());

                var best = Entity.Null;
                var bestScore = 0f;
                var bestLockScale = 1f;
                var bestFlares = false;
                var current = lockState.Target;
                var currentSeen = false;
                var currentInSight = false;
                var currentDistance = 0f;
                var currentLockScale = 1f;
                foreach (var candidate in candidates)
                {
                    if (candidate.Entity == self || (candidate.Class & weapon.Targets) == 0) continue;
                    if (Match.TeamRules.AreAllies(selfTeam, candidate.Team)) continue;
                    var isCurrent = candidate.Entity == current && current != Entity.Null && candidate.Flares == lockState.OnFlares;
                    var inCone = Seeker.InCone(origin, forward, candidate.Position, weapon.LockConeCos, weapon.LockRange, out var distance, out var alignment);
                    if (isCurrent)
                    {
                        currentSeen = true;
                        currentDistance = distance;
                        currentLockScale = candidate.LockScale;
                    }
                    if (!inCone) continue;

                    var score = Seeker.Score(candidate.Heat, alignment, distance);
                    if (!isCurrent && score <= bestScore) continue;
                    var sight = HasLineOfSight(world, self, candidate.Entity, origin, candidate.Position);
                    if (isCurrent) currentInSight = sight;
                    if (!sight || score <= bestScore) continue;
                    best = candidate.Entity;
                    bestScore = score;
                    bestLockScale = candidate.LockScale;
                    bestFlares = candidate.Flares;
                }

                // Keep the current target while it is in sight, or for the grace time after it leaves.
                var holding = currentSeen && currentDistance <= weapon.LockRange * Seeker.GraceRangeScale;
                if (holding)
                {
                    lockState.Grace = Seeker.TickGrace(lockState.Grace, currentInSight, weapon.LockGrace, deltaTime);
                    holding = currentInSight || lockState.Grace > 0f;
                }

                if (holding)
                {
                    best = current;
                    bestLockScale = currentLockScale;
                }
                else if (best == Entity.Null)
                {
                    lockState = default;
                    continue;
                }
                else
                {
                    lockState = new TargetLock { Target = best, Grace = weapon.LockGrace, OnFlares = bestFlares };
                    currentInSight = true;
                }

                // Progress fills only while the target is in sight; during grace it holds.
                if (currentInSight)
                    lockState.Progress = math.saturate(lockState.Progress + deltaTime / math.max(weapon.LockTime * bestLockScale, 0.01f));
                lockState.Locked = lockState.Progress >= 1f;

                // A seeker on the flares is no threat to the pilot: the warning stops, so they know the flares worked.
                if (!lockState.OnFlares && _threats.HasComponent(best))
                {
                    var threat = _threats[best];
                    if (lockState.Locked) threat.LockedBy++;
                    else threat.Locking++;
                    _threats[best] = threat;
                }
            }

            candidates.Dispose();
        }

        /// <summary>
        /// Every lockable thing this tick, with its heat. A chopper with flares burning is replaced by its flare cloud
        /// (much hotter), so a seeker on it, or looking that way, goes for the flares; when they burn out that lock breaks.
        /// A chopper in a decoy window without flares (Ghost ability) is left out.
        /// </summary>
        private NativeList<Candidate> CollectCandidates(ref SystemState state)
        {
            var candidates = new NativeList<Candidate>(32, Allocator.Temp);
            foreach (var (targetable, pose, entity) in SystemAPI.Query<RefRO<Targetable>, RefRO<LocalToWorld>>().WithEntityAccess())
            {
                // Destroyed: not a target (owner, 26 Nov: a downed turret could still be locked).
                if (SystemAPI.HasComponent<Health>(entity) && SystemAPI.GetComponent<Health>(entity).IsDead) continue;
                if (SystemAPI.HasComponent<TurretState>(entity) && SystemAPI.GetComponent<TurretState>(entity).Down) continue;
                var team = TeamOf(ref state, entity);
                if (SystemAPI.HasComponent<CountermeasureState>(entity))
                {
                    var flares = SystemAPI.GetComponent<CountermeasureState>(entity);
                    if (flares.FlaresInAir)
                        candidates.Add(new Candidate
                        {
                            Entity = entity,
                            Position = FlareFlight.Position(flares),
                            Class = targetable.ValueRO.Class,
                            Heat = FlareFlight.Heat,
                            LockScale = targetable.ValueRO.LockScale,
                            Flares = true,
                            Team = team,
                        });
                    if (flares.FlaresInAir || flares.DecoyRemaining > 0f) continue;
                }

                var speed = SystemAPI.HasComponent<PhysicsVelocity>(entity)
                    ? math.length(SystemAPI.GetComponent<PhysicsVelocity>(entity).Linear)
                    : 0f;
                var dashing = false;
                // Boosting engines run hot.
                if (SystemAPI.HasComponent<ChopperFlightState>(entity))
                    dashing = SystemAPI.GetComponent<ChopperFlightState>(entity).Boosting;

                candidates.Add(new Candidate
                {
                    Entity = entity,
                    Position = pose.ValueRO.Position,
                    Class = targetable.ValueRO.Class,
                    Heat = Seeker.Heat(targetable.ValueRO, speed, dashing),
                    LockScale = targetable.ValueRO.LockScale,
                    Team = team,
                });
            }
            return candidates;
        }

        private byte TeamOf(ref SystemState state, Entity entity) =>
            SystemAPI.HasComponent<Match.Team>(entity) ? SystemAPI.GetComponent<Match.Team>(entity).Value : Match.TeamRules.None;

        /// <summary>Nothing but the target (or nothing at all) between the seeker and the target.</summary>
        private static bool HasLineOfSight(in CollisionWorld world, Entity self, Entity target, float3 from, float3 to)
        {
            if (!Explosions.TryRaycast(world, self, from, to, out var hit)) return true;
            return hit.Entity == target;
        }
    }
}
