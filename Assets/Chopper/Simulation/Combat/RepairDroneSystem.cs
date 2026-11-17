using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics.Systems;
using Unity.Transforms;

namespace Chopper.Simulation.Combat
{
    /// <summary>
    /// Server only. Moves each repair drone through its phases (<see cref="RepairDrone.Step"/>) and mends its target.
    /// <see cref="PylonFireSystem"/> launches it. One match log entry (<see cref="Session.MatchLogKind.Repair"/>) per trip.
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PhysicsSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(PylonFireSystem))]
    public partial struct RepairDroneSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var deltaTime = SystemAPI.Time.DeltaTime;
            var now = SystemAPI.Time.ElapsedTime;
            var hasLog = SystemAPI.TryGetSingletonBuffer<Session.MatchLogEntry>(out var log);
            var healths = SystemAPI.GetComponentLookup<Health>();
            var transforms = SystemAPI.GetComponentLookup<LocalTransform>(true);

            foreach (var (drone, owner) in SystemAPI.Query<RefRW<RepairDroneState>>().WithEntityAccess())
            {
                ref var current = ref drone.ValueRW;
                if (!current.Out) continue;

                // The owner died: the drone is lost with it.
                if (!healths.HasComponent(owner) || healths[owner].IsDead)
                {
                    RepairDrone.Reset(ref current);
                    continue;
                }

                var target = current.Target;
                var valid = target != Entity.Null && healths.HasComponent(target) && transforms.HasComponent(target);
                var targetHealth = valid ? healths[target] : default;
                var distance = valid && transforms.HasComponent(owner)
                    ? math.distance(transforms[owner].Position, transforms[target].Position)
                    : 0f;

                var wasHealing = current.State == DronePhase.Healing;
                RepairDrone.Step(ref current, ref targetHealth, valid, distance, deltaTime);
                if (valid) healths[target] = targetHealth;

                // Trip's heal is done: log what it mended (repair XP counts it like the field repair).
                if (!wasHealing || current.State == DronePhase.Healing || current.Healed <= 0f) continue;
                if (hasLog)
                    log.Add(new Session.MatchLogEntry
                    {
                        Kind = Session.MatchLogKind.Repair,
                        Time = now,
                        Actor = SystemAPI.HasComponent<GhostOwner>(owner) ? SystemAPI.GetComponent<GhostOwner>(owner).NetworkId : 0,
                        Amount = current.Healed,
                        Distance = -1f,
                    });
                current.Healed = 0f;
            }
        }
    }
}
