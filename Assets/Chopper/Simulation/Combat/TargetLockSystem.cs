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
                var origin = pose.Position + math.mul(pose.Rotation, weapon.MuzzleOffset);
                var forward = math.mul(pose.Rotation, math.forward());

                var best = Entity.Null;
                var bestScore = 0f;
                var bestLockScale = 1f;
                var current = lockState.Target;
                var currentSeen = false;
                var currentInSight = false;
                var currentDistance = 0f;
                var currentLockScale = 1f;
                foreach (var candidate in candidates)
                {
                    if (candidate.Entity == self || (candidate.Class & weapon.Targets) == 0) continue;
                    var isCurrent = candidate.Entity == current && current != Entity.Null;
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
                    lockState = new TargetLock { Target = best, Grace = weapon.LockGrace };
                    currentInSight = true;
                }

                // Progress fills only while the target is in sight; during grace it holds.
                if (currentInSight)
                    lockState.Progress = math.saturate(lockState.Progress + deltaTime / math.max(weapon.LockTime * bestLockScale, 0.01f));
                lockState.Locked = lockState.Progress >= 1f;

                if (_threats.HasComponent(best))
                {
                    var threat = _threats[best];
                    if (lockState.Locked) threat.LockedBy++;
                    else threat.Locking++;
                    _threats[best] = threat;
                }
            }

            candidates.Dispose();
        }

        /// <summary>Every lockable thing this tick, with its heat. Targets with flares out are left out.</summary>
        private NativeList<Candidate> CollectCandidates(ref SystemState state)
        {
            var candidates = new NativeList<Candidate>(32, Allocator.Temp);
            foreach (var (targetable, pose, entity) in SystemAPI.Query<RefRO<Targetable>, RefRO<LocalToWorld>>().WithEntityAccess())
            {
                if (SystemAPI.HasComponent<CountermeasureState>(entity) &&
                    SystemAPI.GetComponent<CountermeasureState>(entity).DecoyRemaining > 0f)
                    continue;

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
                });
            }
            return candidates;
        }

        /// <summary>Nothing but the target (or nothing at all) between the seeker and the target.</summary>
        private static bool HasLineOfSight(in CollisionWorld world, Entity self, Entity target, float3 from, float3 to)
        {
            if (!Explosions.TryRaycast(world, self, from, to, out var hit)) return true;
            return hit.Entity == target;
        }
    }
}
