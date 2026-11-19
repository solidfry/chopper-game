using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Physics.Extensions;
using Unity.Physics.Systems;
using Unity.Transforms;

namespace Chopper.Simulation.Combat
{
    /// <summary>
    /// Server only. Blasts throw choppers (live ones and wrecks): each hit with a <see cref="DamageEvent.Push"/> (rockets,
    /// missiles, wreck blasts and parts, <see cref="Explosions.PushSpeed"/>) changes its speed, and a hit off the centre
    /// also makes it tumble. Runs just before <see cref="ApplyDamageSystem"/> clears the hits, so each push counts once.
    /// The push holds even when friendly fire blocks the damage. Owners see it as a prediction correction.
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PhysicsSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(RocketSystem))]
    [UpdateAfter(typeof(GuidedMissileSystem))]
    [UpdateAfter(typeof(WreckSystem))]
    [UpdateBefore(typeof(ApplyDamageSystem))]
    public partial struct BlastPushSystem : ISystem
    {
        /// <summary>Spin (rad/s) per m/s of push per metre off the centre of mass.</summary>
        public const float Tumble = 0.12f;
        public const float MaxSpin = 3f;

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            foreach (var (damage, velocity, mass, transform) in SystemAPI
                         .Query<DynamicBuffer<DamageEvent>, RefRW<PhysicsVelocity>, RefRO<PhysicsMass>, RefRO<LocalTransform>>())
            {
                foreach (var hit in damage)
                {
                    if (math.lengthsq(hit.Push) < 1e-4f) continue;
                    var pose = transform.ValueRO;
                    velocity.ValueRW.Linear += hit.Push;
                    var centre = pose.TransformPoint(mass.ValueRO.CenterOfMass);
                    var spin = velocity.ValueRO.GetAngularVelocityWorldSpace(mass.ValueRO, pose.Rotation);
                    spin += Spin(hit.Point - centre, hit.Push);
                    velocity.ValueRW.SetAngularVelocityWorldSpace(mass.ValueRO, pose.Rotation, spin);
                }
            }
        }

        /// <summary>Tumble from a push at <paramref name="lever"/> off the centre of mass, at most <see cref="MaxSpin"/>.</summary>
        public static float3 Spin(float3 lever, float3 push)
        {
            var spin = math.cross(lever, push) * Tumble;
            var length = math.length(spin);
            return length > MaxSpin ? spin * (MaxSpin / length) : spin;
        }
    }
}
