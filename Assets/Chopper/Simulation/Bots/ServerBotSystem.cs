using Chopper.Simulation.Combat;
using Chopper.Simulation.Flight;
using Chopper.Simulation.Match;
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
    /// <summary>
    /// Server only. Flies each server bot's chopper with the same steps as the client bot (<see cref="BotFlight"/>) and
    /// writes its <see cref="ChopperInput"/>.
    /// <para>Order: each tick netcode first copies the received commands into <see cref="ChopperInput"/>; a bot's
    /// chopper has no commands, so that step clears its input. This system runs right after it and before the fixed
    /// step (flight and weapons read the input there). An <c>InputEvent</c> that is set counts as pressed in this tick.</para>
    /// <para>Cost: one path search per tick for all bots (<see cref="BotFrame.PlansLeft"/>). After the match ends the
    /// bots hover (<see cref="BotFrame.Hold"/>); at the next match they start fresh.</para>
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup), OrderFirst = true)]
    [UpdateAfter(typeof(CopyCommandBufferToInputSystemGroup))]
    public partial struct ServerBotSystem : ISystem
    {
        /// <summary>Path searches per tick, all bots together.</summary>
        private const int PlansPerTick = 1;

        private BotFlight _flight;
        private bool _held;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _flight = new BotFlight(ref state);
            state.RequireForUpdate<ServerBot>();
            state.RequireForUpdate<PhysicsWorldSingleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var hold = SystemAPI.TryGetSingleton<MatchState>(out var match) && match.Phase != MatchPhase.InProgress;
            var restart = _held && !hold;
            _held = hold;

            SystemAPI.TryGetSingleton<NavGrid>(out var grid);
            var hasBounds = SystemAPI.TryGetSingleton<ArenaBounds>(out var bounds);
            // The physics world is written by jobs: wait for them before the queries.
            state.CompleteDependency();
            var frame = new BotFrame
            {
                World = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld,
                Gravity = math.length(SystemAPI.TryGetSingleton<PhysicsStep>(out var step) ? step.Gravity : PhysicsStep.Default.Gravity),
                HasBounds = hasBounds,
                Bounds = bounds,
                Now = SystemAPI.Time.ElapsedTime,
                DeltaTime = SystemAPI.Time.DeltaTime,
                Grid = grid,
                PlansLeft = PlansPerTick,
                Hold = hold,
            };
            // Live turrets: ground targets for the bots.
            var turrets = new NativeList<BotTurretInfo>(4, Allocator.Temp);
            foreach (var (turret, config, health, pose, entity) in SystemAPI
                         .Query<RefRO<TurretState>, RefRO<TurretConfig>, RefRO<Health>, RefRO<LocalTransform>>().WithEntityAccess())
            {
                if (turret.ValueRO.Down || health.ValueRO.IsDead) continue;
                turrets.Add(new BotTurretInfo
                {
                    Entity = entity,
                    Position = pose.ValueRO.Position + math.mul(pose.ValueRO.Rotation, new float3(0f, 2f, 0f)),
                    Health01 = health.ValueRO.Max > 0f ? health.ValueRO.Current / health.ValueRO.Max : 1f,
                    Team = config.ValueRO.Team,
                    Reach = config.ValueRO.Gun.Range,
                });
            }
            frame.Turrets = turrets.AsArray();
            // Capture the Nuke: the objective for the bots.
            if (SystemAPI.TryGetSingleton<MatchState>(out var mode) && mode.Mode == MatchMode.CaptureTheNuke)
            {
                foreach (var (nuke, carrier, pose) in SystemAPI.Query<RefRO<NukeState>, RefRO<NukeCarrier>, RefRO<LocalTransform>>())
                {
                    frame.Objective.Active = true;
                    frame.Objective.NukePosition = pose.ValueRO.Position;
                    frame.Objective.Phase = nuke.ValueRO.Phase;
                    frame.Objective.Carrier = carrier.ValueRO.Chopper;
                    frame.Objective.CarrierTeam = nuke.ValueRO.CarrierTeam;
                    frame.Objective.ArmedBy = nuke.ValueRO.ArmedBy;
                    frame.Objective.DockTeam = nuke.ValueRO.DockTeam;
                }
                foreach (var zone in SystemAPI.Query<RefRO<NukeBase>>())
                {
                    if (zone.ValueRO.Team == 1) frame.Objective.Base1 = zone.ValueRO.Dock;
                    else if (zone.ValueRO.Team == 2) frame.Objective.Base2 = zone.ValueRO.Dock;
                }
            }

            // Live choppers: what the bots see, and which one each player entity owns.
            var choppers = new NativeList<BotChopperInfo>(16, Allocator.Temp);
            var owned = new NativeHashMap<Entity, Entity>(16, Allocator.Temp);
            foreach (var (pose, health, ghostOwner, owner, entity) in
                     SystemAPI.Query<RefRO<LocalTransform>, RefRO<Health>, RefRO<GhostOwner>, RefRO<ConnectionOwner>>()
                         .WithAll<ChopperFlightConfig>().WithEntityAccess())
            {
                if (health.ValueRO.Current <= 0f) continue;
                choppers.Add(new BotChopperInfo
                {
                    Entity = entity,
                    OwnerId = ghostOwner.ValueRO.NetworkId,
                    Position = pose.ValueRO.Position,
                    Health01 = health.ValueRO.Max > 0f ? health.ValueRO.Current / health.ValueRO.Max : 1f,
                    Team = SystemAPI.HasComponent<Match.Team>(entity) ? SystemAPI.GetComponent<Match.Team>(entity).Value : Match.TeamRules.None,
                });
                owned[owner.ValueRO.Value] = entity;
            }
            var missiles = new NativeList<BotMissileInfo>(8, Allocator.Temp);
            foreach (var (missile, pose) in SystemAPI.Query<RefRO<GuidedMissile>, RefRO<LocalTransform>>())
                missiles.Add(new BotMissileInfo { Target = missile.ValueRO.Decoyed ? Entity.Null : missile.ValueRO.Target, Position = pose.ValueRO.Position, Velocity = missile.ValueRO.Velocity });

            _flight.Update(ref state);
            // SystemAPI.Query takes 7 types at most: the navigator and the path come by entity.
            foreach (var (bot, playerId, skill, pilot, order, wander, brain, entity) in
                     SystemAPI.Query<RefRO<ServerBot>, RefRO<PlayerId>, RefRO<BotSkill>, RefRW<BotPilotState>, RefRW<PilotOrder>,
                         RefRW<BotWander>, RefRW<BotBrain>>().WithAll<BotNavigator, BotWaypoint>().WithEntityAccess())
            {
                if (bot.ValueRO.Kind != BotKind.Air) continue;
                if (!owned.TryGetValue(entity, out var chopper) || !_flight.CanFly(chopper)) continue;

                var navigator = SystemAPI.GetComponentRW<BotNavigator>(entity);
                var waypoints = SystemAPI.GetBuffer<BotWaypoint>(entity);
                if (restart)
                {
                    // New match: forget the old fight and pick a new goal.
                    brain.ValueRW.Reset();
                    wander.ValueRW.HasGoal = false;
                    navigator.ValueRW.Planned = false;
                }

                var input = _flight.Step(chopper, playerId.ValueRO.Value, ref frame, skill.ValueRO.Value, choppers.AsArray(),
                    missiles.AsArray(), ref pilot.ValueRW, ref order.ValueRW, ref wander.ValueRW, ref brain.ValueRW,
                    ref navigator.ValueRW, waypoints);
                SystemAPI.SetComponent(chopper, input);
            }
            choppers.Dispose();
            owned.Dispose();
            missiles.Dispose();
            turrets.Dispose();
        }
    }
}
