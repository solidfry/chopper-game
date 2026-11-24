using Chopper.Simulation.Combat;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Extensions;
using Unity.Physics.Systems;
using Unity.Transforms;

namespace Chopper.Simulation.Match
{
    /// <summary>A nuke's shock front running out from <see cref="Origin"/> (server only; made by <see cref="NukeSystem"/>).</summary>
    public struct NukeShockwave : IComponentData
    {
        public float3 Origin;
        /// <summary>Server elapsed time at the detonation (s).</summary>
        public double Start;
        /// <summary>Radius the front has already passed (m): each chopper is pushed once.</summary>
        public float Reached;
    }

    /// <summary>
    /// The nuke's shock front: how fast it runs and how hard it throws a chopper (owner, 24 Nov: "push any choppers away
    /// from it when the shock wave hits"). Pure: tests. The client's look (NukeBlastSystem: shake at arrival) uses the same
    /// speed through CombatEffectsSettings.nukeShockSpeed (340).
    /// </summary>
    public static class NukeShock
    {
        /// <summary>Front speed (m/s): about the speed of sound.</summary>
        public const float Speed = 340f;
        /// <summary>Past this distance (m) the front no longer pushes.</summary>
        public const float Radius = 900f;
        /// <summary>Push (m/s) right at the blast; it falls off to 0 at <see cref="Radius"/>.</summary>
        public const float MaxPush = 55f;
        /// <summary>Share of the push that lifts (the front rolls up off the ground).</summary>
        public const float Lift = 0.3f;

        /// <summary>The front's radius <paramref name="seconds"/> after the detonation.</summary>
        public static float Front(float seconds) => math.max(0f, seconds) * Speed;

        /// <summary>Velocity change for a chopper at <paramref name="position"/>; zero outside <see cref="Radius"/>.</summary>
        public static float3 Push(float3 origin, float3 position)
        {
            var offset = position - origin;
            var distance = math.length(offset);
            if (distance >= Radius) return float3.zero;
            var away = distance > 0.1f ? offset / distance : new float3(0f, 1f, 0f);
            var flat = math.normalizesafe(new float3(away.x, 0f, away.z), new float3(1f, 0f, 0f));
            var direction = math.normalizesafe(flat * (1f - Lift) + new float3(0f, Lift, 0f) + away * 0.5f);
            var falloff = 1f - distance / Radius;
            return direction * (MaxPush * falloff * falloff);
        }
    }

    /// <summary>
    /// Server only. Moves each <see cref="NukeShockwave"/> front on and throws every chopper (live and wrecks) it passes
    /// this tick away from the blast, with a little tumble (<see cref="BlastPushSystem.Spin"/>). Looks only on the client
    /// (owners see it as a prediction correction, as with blast pushes). The front is removed past <see cref="NukeShock.Radius"/>.
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PhysicsSystemGroup), OrderLast = true)]
    [UpdateBefore(typeof(ApplyDamageSystem))]
    public partial struct NukeShockSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var now = SystemAPI.Time.ElapsedTime;
            var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
            foreach (var (shock, entity) in SystemAPI.Query<RefRW<NukeShockwave>>().WithEntityAccess())
            {
                var inner = shock.ValueRO.Reached;
                var outer = math.min(NukeShock.Front((float)(now - shock.ValueRO.Start)), NukeShock.Radius);
                var origin = shock.ValueRO.Origin;
                foreach (var (velocity, mass, transform) in SystemAPI
                             .Query<RefRW<PhysicsVelocity>, RefRO<PhysicsMass>, RefRO<LocalTransform>>()
                             .WithAll<DamageEvent>())
                {
                    var pose = transform.ValueRO;
                    var distance = math.distance(pose.Position, origin);
                    if (distance < inner || distance >= outer) continue;
                    var push = NukeShock.Push(origin, pose.Position);
                    velocity.ValueRW.Linear += push;
                    // Hit above the centre: the front tips the chopper over as it passes.
                    var centre = pose.TransformPoint(mass.ValueRO.CenterOfMass);
                    var spin = velocity.ValueRO.GetAngularVelocityWorldSpace(mass.ValueRO, pose.Rotation);
                    spin += BlastPushSystem.Spin(new float3(0f, 1.5f, 0f), push);
                    velocity.ValueRW.SetAngularVelocityWorldSpace(mass.ValueRO, pose.Rotation, spin);
                }
                shock.ValueRW.Reached = outer;
                if (outer >= NukeShock.Radius) ecb.DestroyEntity(entity);
            }
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
}
