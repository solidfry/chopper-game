using Chopper.Simulation.Combat;
using Chopper.Simulation.Flight;
using Chopper.Simulation.Session;
using Unity.Burst;
using Unity.Collections;
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

    /// <summary>What both bot systems collect once per update for <see cref="BotFlight.Step"/>.</summary>
    public static class BotWorld
    {
        /// <summary>The parts of a bot entity that the shared step needs (the caller adds its own tags).</summary>
        public static void AddBotParts(EntityManager manager, Entity bot, uint seed)
        {
            manager.AddComponentData(bot, new BotPilotState());
            manager.AddComponentData(bot, new PilotOrder());
            manager.AddComponentData(bot, BotWander.Create(seed));
            manager.AddComponentData(bot, BotBrain.Create(seed * 31u + 7u));
            manager.AddComponentData(bot, new BotNavigator());
            manager.AddBuffer<BotWaypoint>(bot);
        }
    }

    /// <summary>
    /// A bot flies the local chopper (client with <see cref="ScriptedPilotOverride"/>, e.g. a <c>-bot</c> build). The
    /// steps are in <see cref="BotFlight"/> (shared with the server bots, <see cref="ServerBotSystem"/>).
    /// Needs the <see cref="BotHandling"/> singleton (the spawner's bot profile); without it <see cref="ScriptedPilotSystem"/>
    /// flies the old fixed pattern.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    [UpdateBefore(typeof(CopyInputToCommandBufferSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct BotPilotSystem : ISystem
    {
        private BotFlight _flight;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _flight = new BotFlight(ref state);
            state.RequireForUpdate<NetworkStreamInGame>();
            state.RequireForUpdate<ScriptedPilotOverride>();
            state.RequireForUpdate<BotHandling>();
            state.RequireForUpdate<PhysicsWorldSingleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var connection = SystemAPI.GetSingletonEntity<NetworkStreamInGame>();
            var networkId = SystemAPI.GetComponent<NetworkId>(connection).Value;
            if (SystemAPI.QueryBuilder().WithAll<BotPilotState>().Build().IsEmpty)
            {
                var bot = state.EntityManager.CreateEntity();
                BotWorld.AddBotParts(state.EntityManager, bot, (uint)networkId * 7919u + 17u);
            }

            var handling = SystemAPI.GetSingleton<BotHandling>();
            SystemAPI.TryGetSingleton<NavGrid>(out var grid);
            var hasBounds = SystemAPI.TryGetSingleton<ArenaBounds>(out var bounds);
            var frame = new BotFrame
            {
                World = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld,
                Gravity = math.length(SystemAPI.TryGetSingleton<PhysicsStep>(out var step) ? step.Gravity : PhysicsStep.Default.Gravity),
                HasBounds = hasBounds,
                Bounds = bounds,
                Now = SystemAPI.Time.ElapsedTime,
                DeltaTime = SystemAPI.Time.DeltaTime,
                Grid = grid,
                PlansLeft = int.MaxValue,
            };

            // The local chopper (one per client).
            var chopper = Entity.Null;
            foreach (var (_, entity) in SystemAPI.Query<RefRO<ChopperInput>>().WithAll<GhostOwnerIsLocal, ChopperFlightConfig>().WithEntityAccess())
                chopper = entity;
            _flight.Update(ref state);
            if (chopper == Entity.Null || !_flight.CanFly(chopper)) return;

            var choppers = new NativeList<BotChopperInfo>(8, Allocator.Temp);
            foreach (var (pose, health, owner, entity) in
                     SystemAPI.Query<RefRO<LocalTransform>, RefRO<Health>, RefRO<GhostOwner>>().WithAll<ChopperFlightConfig>().WithEntityAccess())
            {
                if (health.ValueRO.Current <= 0f) continue;
                choppers.Add(new BotChopperInfo
                {
                    Entity = entity,
                    OwnerId = owner.ValueRO.NetworkId,
                    Position = pose.ValueRO.Position,
                    Health01 = health.ValueRO.Max > 0f ? health.ValueRO.Current / health.ValueRO.Max : 1f,
                });
            }
            var missiles = new NativeList<BotMissileInfo>(4, Allocator.Temp);
            foreach (var (missile, pose) in SystemAPI.Query<RefRO<GuidedMissile>, RefRO<LocalTransform>>())
                missiles.Add(new BotMissileInfo { Target = missile.ValueRO.Target, Position = pose.ValueRO.Position, Velocity = missile.ValueRO.Velocity });

            foreach (var (pilot, order, wander, brain, navigator, waypoints) in
                     SystemAPI.Query<RefRW<BotPilotState>, RefRW<PilotOrder>, RefRW<BotWander>, RefRW<BotBrain>, RefRW<BotNavigator>, DynamicBuffer<BotWaypoint>>())
            {
                var input = _flight.Step(chopper, networkId, ref frame, handling, choppers.AsArray(), missiles.AsArray(),
                    ref pilot.ValueRW, ref order.ValueRW, ref wander.ValueRW, ref brain.ValueRW, ref navigator.ValueRW, waypoints);
                SystemAPI.SetComponent(chopper, input);
            }
            choppers.Dispose();
            missiles.Dispose();
        }
    }
}
