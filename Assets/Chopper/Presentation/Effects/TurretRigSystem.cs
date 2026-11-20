using Chopper.Simulation.Combat;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Client only. Turns each turret's head (yaw) and guns (pitch) to the replicated <see cref="TurretState.Aim"/>;
    /// a destroyed turret droops its guns. The children keep their baked positions; only their rotation is set.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial struct TurretRigSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var transforms = SystemAPI.GetComponentLookup<LocalTransform>();
            foreach (var (rig, turret, pose) in SystemAPI.Query<RefRO<TurretRig>, RefRO<TurretState>, RefRO<LocalToWorld>>())
            {
                TurretRules.RigAngles(pose.ValueRO.Value, math.normalizesafe(turret.ValueRO.Aim, pose.ValueRO.Forward),
                    turret.ValueRO.Down, out var yaw, out var pitch);
                if (transforms.TryGetComponent(rig.ValueRO.Head, out var head))
                {
                    head.Rotation = quaternion.RotateY(yaw);
                    transforms[rig.ValueRO.Head] = head;
                }
                if (transforms.TryGetComponent(rig.ValueRO.Guns, out var guns))
                {
                    guns.Rotation = quaternion.RotateX(-pitch);
                    transforms[rig.ValueRO.Guns] = guns;
                }
            }
        }
    }
}
