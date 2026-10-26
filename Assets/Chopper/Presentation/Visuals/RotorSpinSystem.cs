using Chopper.Simulation.Visuals;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Chopper.Presentation.Visuals
{
    /// <summary>Client only. Turns every chopper's rotors (<see cref="RotorSpin"/>) before transforms are computed.</summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.LocalSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial struct RotorSpinSystem : ISystem
    {
        private ComponentLookup<LocalTransform> _transforms;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _transforms = state.GetComponentLookup<LocalTransform>();
            state.RequireForUpdate<RotorSpin>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var deltaTime = SystemAPI.Time.DeltaTime;
            _transforms.Update(ref state);

            foreach (var rotors in SystemAPI.Query<DynamicBuffer<RotorSpin>>())
            {
                foreach (var rotor in rotors)
                {
                    if (!_transforms.HasComponent(rotor.Rotor)) continue;
                    var transform = _transforms[rotor.Rotor];
                    // Same order as Transform.Rotate (local Z, then X, then Y).
                    transform.Rotation = math.mul(transform.Rotation, quaternion.EulerZXY(rotor.RadiansPerSecond * deltaTime));
                    _transforms[rotor.Rotor] = transform;
                }
            }
        }
    }
}
