using Chopper.Simulation.Flight;
using Chopper.Simulation.Visuals;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Chopper.Presentation.Visuals
{
    /// <summary>
    /// Client only. Folds every chopper's landing gear (<see cref="LandingGearLeg"/>) up above
    /// <see cref="LandingGearState.UpHeight"/> and down below <see cref="LandingGearState.DownHeight"/>, from the
    /// replicated height over the ground (<see cref="ChopperFlightState.Altitude"/>). Before transforms, as the rotors.
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.LocalSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial struct LandingGearSystem : ISystem
    {
        private ComponentLookup<LocalTransform> _transforms;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _transforms = state.GetComponentLookup<LocalTransform>();
            state.RequireForUpdate<LandingGearState>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var deltaTime = SystemAPI.Time.DeltaTime;
            _transforms.Update(ref state);
            foreach (var (gear, legs, flight) in SystemAPI
                         .Query<RefRW<LandingGearState>, DynamicBuffer<LandingGearLeg>, RefRO<ChopperFlightState>>())
            {
                ref var g = ref gear.ValueRW;
                g.Up = LandingGear.WantsUp(g.Up, flight.ValueRO.Altitude, g.UpHeight, g.DownHeight);
                var extension = LandingGear.Step(g.Extension, g.Up, g.Seconds, deltaTime);
                if (extension == g.Extension) continue;
                g.Extension = extension;
                foreach (var leg in legs)
                {
                    if (!_transforms.HasComponent(leg.Leg)) continue;
                    LandingGear.Pose(leg, extension, out var position, out var rotation);
                    var transform = _transforms[leg.Leg];
                    transform.Position = position;
                    transform.Rotation = rotation;
                    _transforms[leg.Leg] = transform;
                }
            }
        }
    }
}
