using Chopper.Simulation.Combat;
using Chopper.Simulation.Flight;
using Chopper.Simulation.Session;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Transforms;

namespace Chopper.Simulation.Bots
{
    /// <summary>One bot: the chopper it flies (found again after each respawn) and its hands.</summary>
    public struct BotPilotState : IComponentData
    {
        public Entity Chopper;
        public PilotHands Hands;
    }

    /// <summary>
    /// A bot flies the local chopper (client with <see cref="ScriptedPilotOverride"/>, e.g. a <c>-bot</c> build):
    /// brain (<see cref="BotWander"/> for now) → order → eyes (<see cref="BotSensing"/>) → hands (<see cref="BotPilot"/>)
    /// → <see cref="ChopperInput"/>, the same input a player sends. Needs the <see cref="BotHandling"/> singleton (the
    /// spawner's bot profile); without it <see cref="ScriptedPilotSystem"/> flies the old fixed pattern.
    /// Server bots (filling empty slots) will run the same steps on the server.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    [UpdateBefore(typeof(CopyInputToCommandBufferSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct BotPilotSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();
            state.RequireForUpdate<ScriptedPilotOverride>();
            state.RequireForUpdate<BotHandling>();
            state.RequireForUpdate<PhysicsWorldSingleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (SystemAPI.QueryBuilder().WithAll<BotPilotState>().Build().IsEmpty)
            {
                var connection = SystemAPI.GetSingletonEntity<NetworkStreamInGame>();
                var networkId = SystemAPI.GetComponent<NetworkId>(connection).Value;
                var bot = state.EntityManager.CreateEntity();
                state.EntityManager.AddComponentData(bot, new BotPilotState());
                state.EntityManager.AddComponentData(bot, new PilotOrder());
                state.EntityManager.AddComponentData(bot, BotWander.Create((uint)networkId * 7919u + 17u));
            }

            var handling = SystemAPI.GetSingleton<BotHandling>();
            var world = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
            var gravity = math.length(SystemAPI.TryGetSingleton<PhysicsStep>(out var step) ? step.Gravity : PhysicsStep.Default.Gravity);
            var hasBounds = SystemAPI.TryGetSingleton<ArenaBounds>(out var bounds);
            var now = SystemAPI.Time.ElapsedTime;
            var deltaTime = SystemAPI.Time.DeltaTime;

            // The local chopper (one per client).
            var chopper = Entity.Null;
            foreach (var (_, entity) in SystemAPI.Query<RefRO<ChopperInput>>().WithAll<GhostOwnerIsLocal, ChopperFlightConfig>().WithEntityAccess())
                chopper = entity;

            foreach (var (pilot, order, wander) in SystemAPI.Query<RefRW<BotPilotState>, RefRW<PilotOrder>, RefRW<BotWander>>())
            {
                if (chopper == Entity.Null) continue;
                if (pilot.ValueRO.Chopper != chopper)
                {
                    // New chopper (spawn or respawn): fresh hands and a new goal from here.
                    pilot.ValueRW = new BotPilotState { Chopper = chopper };
                    wander.ValueRW.HasGoal = false;
                }

                var transform = SystemAPI.GetComponent<LocalTransform>(chopper);
                var velocity = SystemAPI.GetComponent<PhysicsVelocity>(chopper).Linear;
                var flight = SystemAPI.GetComponent<ChopperFlightConfig>(chopper);
                var flightState = SystemAPI.GetComponent<ChopperFlightState>(chopper);

                var sense = new PilotSense
                {
                    Position = transform.Position,
                    Rotation = transform.Rotation,
                    Velocity = velocity,
                    Altitude = flightState.Altitude,
                    BoostEnergy = flightState.BoostEnergy,
                    Boosting = flightState.Boosting,
                    Gravity = gravity,
                    Obstacle = ObstacleSense.Clear,
                    HasBounds = hasBounds,
                    BoundsCentre = bounds.Center,
                    BoundsHalfExtents = bounds.HalfExtents,
                };

                wander.ValueRW.Think(world, chopper, sense.Position, now, sense, handling, ref order.ValueRW);

                var maxAcceleration = gravity * flight.GravityScale * flight.TiltThrust * math.tan(handling.MaxTilt);
                var wanted = BotPilot.DesiredVelocity(order.ValueRO, sense, handling);
                sense.Obstacle = BotSensing.Probe(world, chopper, sense.Position, velocity, wanted, maxAcceleration, handling);

                var input = BotPilot.Steer(order.ValueRO, sense, flight, handling, ref pilot.ValueRW.Hands, deltaTime);
                SystemAPI.SetComponent(chopper, input);
            }
        }
    }
}
