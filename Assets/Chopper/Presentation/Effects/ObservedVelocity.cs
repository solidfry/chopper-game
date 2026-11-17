using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Transforms;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Client only: the velocity a ghost shows on screen. An interpolated ghost (another player, a bot) does not run
    /// physics on the client, so its <see cref="PhysicsVelocity"/> reads 0. This is measured from its interpolated
    /// position each frame instead (smoothed). A predicted ghost (your own chopper) copies its physics velocity.
    /// Read it with <see cref="ObservedVelocity.Of"/> for the lead reticles, remote tracers and flares.
    /// </summary>
    public struct ObservedVelocity : IComponentData
    {
        public float3 Linear;
        /// <summary>The shared smoothing (and teleport reset) the camera and HUD use for your own chopper.</summary>
        public Bridge.MeasuredVelocity Measure;

        /// <summary>The on-screen velocity, else the physics velocity, else 0.</summary>
        public static float3 Of(Entity entity, in ComponentLookup<ObservedVelocity> observed, in ComponentLookup<PhysicsVelocity> physics) =>
            observed.TryGetComponent(entity, out var seen) ? seen.Linear
            : physics.TryGetComponent(entity, out var velocity) ? velocity.Linear
            : float3.zero;

        /// <summary>One frame of the measurement after moving to <paramref name="position"/>.</summary>
        public static void Step(ref ObservedVelocity state, float3 position, float deltaTime)
        {
            state.Measure.Update(position, deltaTime);
            state.Linear = state.Measure.Velocity;
        }
    }

    /// <summary>Adds and updates <see cref="ObservedVelocity"/> on every client ghost with a physics velocity.</summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup), OrderFirst = true)]
    public partial struct ObservedVelocitySystem : ISystem
    {
        private EntityQuery _missing;

        public void OnCreate(ref SystemState state)
        {
            _missing = SystemAPI.QueryBuilder().WithAll<PhysicsVelocity, LocalToWorld, GhostInstance>().WithNone<ObservedVelocity>().Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!_missing.IsEmpty) state.EntityManager.AddComponent<ObservedVelocity>(_missing);

            var deltaTime = SystemAPI.Time.DeltaTime;
            // Your own chopper (predicted): its physics velocity is right.
            foreach (var (observed, pose, physics) in SystemAPI
                         .Query<RefRW<ObservedVelocity>, RefRO<LocalToWorld>, RefRO<PhysicsVelocity>>().WithAll<PredictedGhost>())
            {
                observed.ValueRW.Linear = physics.ValueRO.Linear;
            }

            // Everyone else (interpolated): measured from the position on screen.
            foreach (var (observed, pose) in SystemAPI.Query<RefRW<ObservedVelocity>, RefRO<LocalToWorld>>().WithNone<PredictedGhost>())
                ObservedVelocity.Step(ref observed.ValueRW, pose.ValueRO.Position, deltaTime);
        }
    }
}
