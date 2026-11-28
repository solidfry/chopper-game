using Chopper.Simulation.Combat;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Transforms;

namespace Chopper.Simulation.Session
{
    /// <summary>
    /// Server only. The one place that spawns choppers:
    /// <list type="bullet">
    /// <item>A chopper at zero health is destroyed (the ghost despawns on all clients). Its player gets a
    /// <see cref="RespawnTimer"/> from the mode's <see cref="Match.RespawnRule"/> (<see cref="Match.MatchRules"/>); a player
    /// connection also gets a <see cref="Match.RespawnTimeRpc"/> for the HUD countdown.</item>
    /// <item>A new player gets a timer of 0 s (<see cref="GoInGameServerSystem"/>; a server bot:
    /// <see cref="Bots.ServerBotFillSystem"/>). A player is any entity with a <see cref="PlayerId"/>.</item>
    /// <item>When a timer ends, the player's chopper spawns at the best <see cref="SpawnPoint"/> of that moment
    /// (<see cref="SpawnScoring"/>: away from enemies, walls and the ground; rules in <see cref="SpawnRules"/>). If no
    /// point can be used (or the map has none), it spawns at the spawner's own point.</item>
    /// </list>
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(GoInGameServerSystem))]
    public partial struct ChopperRespawnSystem : ISystem
    {
        /// <summary>Seconds the kill cam holds on the wreck's blast before the new chopper spawns.</summary>
        private const float KillCamHold = 1.2f;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ChopperSpawner>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var spawnerEntity = SystemAPI.GetSingletonEntity<ChopperSpawner>();
            var spawner = SystemAPI.GetComponent<ChopperSpawner>(spawnerEntity);
            var chassisPrefabs = SystemAPI.GetBuffer<ChassisPrefab>(spawnerEntity);
            var deltaTime = SystemAPI.Time.DeltaTime;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // Respawn delay comes from the mode's rule (MatchRules); no match yet = Free For All defaults.
            var rules = SystemAPI.TryGetSingleton<Match.MatchRules>(out var r) ? r : Match.MatchRules.Default;
            var mode = Match.MatchMode.FreeForAll;
            var matchElapsed = 0f;
            if (SystemAPI.TryGetSingleton<Match.MatchState>(out var matchState))
            {
                mode = matchState.Mode;
                matchElapsed = rules.TimeLimit - matchState.TimeRemaining;
            }
            var respawnRule = rules.Respawn(mode);

            // Capture the Nuke, new round: every chopper leaves and its player spawns again on the team's base grid,
            // all at the same moment (Match.NukeBaseSpawn.RoundStartDelay).
            if (!SystemAPI.QueryBuilder().WithAll<Match.NukeRoundStart>().Build().IsEmpty)
            {
                foreach (var (_, start) in SystemAPI.Query<RefRO<Match.NukeRoundStart>>().WithEntityAccess()) ecb.DestroyEntity(start);
                foreach (var (owner, chopper) in SystemAPI.Query<RefRO<ConnectionOwner>>().WithAll<Health>().WithEntityAccess())
                {
                    var connection = owner.ValueRO.Value;
                    if (SystemAPI.HasBuffer<LinkedEntityGroup>(connection))
                    {
                        var group = SystemAPI.GetBuffer<LinkedEntityGroup>(connection);
                        for (var i = group.Length - 1; i >= 0; i--)
                            if (group[i].Value == chopper) group.RemoveAt(i);
                    }
                    ecb.DestroyEntity(chopper);
                }
                foreach (var (_, player) in SystemAPI.Query<RefRO<PlayerId>>().WithAll<Match.Team>().WithEntityAccess())
                {
                    var timer = new RespawnTimer { Remaining = Match.NukeBaseSpawn.RoundStartDelay };
                    if (SystemAPI.HasComponent<RespawnTimer>(player)) SystemAPI.SetComponent(player, timer);
                    else ecb.AddComponent(player, timer);
                    if (SystemAPI.HasComponent<NetworkId>(player))
                    {
                        var rpc = ecb.CreateEntity();
                        ecb.AddComponent(rpc, new Match.RespawnTimeRpc { Seconds = timer.Remaining });
                        ecb.AddComponent(rpc, new SendRpcCommandRequest { TargetConnection = player });
                    }
                }
                ecb.Playback(state.EntityManager);
                ecb.Dispose();
                return;
            }

            foreach (var (health, owner, chopper) in SystemAPI.Query<RefRO<Health>, RefRO<ConnectionOwner>>().WithEntityAccess())
            {
                if (!health.ValueRO.IsDead) continue;

                var connection = owner.ValueRO.Value;
                if (SystemAPI.HasBuffer<LinkedEntityGroup>(connection))
                {
                    // Take the chopper out of the connection's group so the group never holds a destroyed entity.
                    var group = SystemAPI.GetBuffer<LinkedEntityGroup>(connection);
                    for (var i = group.Length - 1; i >= 0; i--)
                    {
                        if (group[i].Value == chopper)
                            group.RemoveAt(i);
                    }
                    var deaths = SystemAPI.HasComponent<Match.PlayerScore>(connection)
                        ? SystemAPI.GetComponent<Match.PlayerScore>(connection).Deaths
                        : 0;
                    var delay = respawnRule.Delay(deaths, matchElapsed);
                    ecb.AddComponent(connection, new RespawnTimer { Remaining = delay });
                    // Players (not bots) see the countdown on the death message.
                    if (SystemAPI.HasComponent<NetworkId>(connection))
                    {
                        var rpc = ecb.CreateEntity();
                        ecb.AddComponent(rpc, new Match.RespawnTimeRpc { Seconds = delay });
                        ecb.AddComponent(rpc, new SendRpcCommandRequest { TargetConnection = connection });
                    }
                }
                // A chopper that can be a wreck falls and explodes first (WreckSystem destroys it); it is no longer
                // this player's chopper, so it leaves this loop.
                if (SystemAPI.HasComponent<WreckConfig>(chopper)) ecb.RemoveComponent<ConnectionOwner>(chopper);
                else ecb.DestroyEntity(chopper);
            }

            // Built on the first spawn of this tick: most ticks have none.
            var placement = default(SpawnPlacement);
            var prepared = false;

            // Players whose wreck still falls: the kill cam follows it, so the new chopper waits for the blast and a
            // short hold on it (Docs/ChopperDeathDesign.md).
            var falling = new NativeHashSet<int>(8, Allocator.Temp);
            foreach (var wreck in SystemAPI.Query<RefRO<Wreck>>())
                if (wreck.ValueRO.Active) falling.Add(wreck.ValueRO.OwnerNetworkId);

            foreach (var (timer, playerId, connection) in SystemAPI.Query<RefRW<RespawnTimer>, RefRO<PlayerId>>()
                         // Team: MatchScoreSystem gives it on join (0 in Free For All); the first chopper waits for it.
                         .WithAll<Match.Team>().WithNone<Progression.LoadoutPending, Bots.BotClassPick>().WithEntityAccess())
            {
                timer.ValueRW.Remaining -= deltaTime;
                if (falling.Contains(playerId.ValueRO.Value)) timer.ValueRW.Remaining = math.max(timer.ValueRO.Remaining, KillCamHold);
                if (timer.ValueRO.Remaining > 0f) continue;

                if (!prepared)
                {
                    placement = Prepare(ref state, spawnerEntity);
                    prepared = true;
                }

                var id = playerId.ValueRO.Value;
                // Bot ids start at 1001: fold them next to the players for the fallback row.
                var position = ChopperSpawning.FallbackPosition(spawner, Bots.BotIds.IsBot(id) ? 9 + Bots.BotIds.Slot(id) : id);
                var rotation = spawner.Rotation;
                var team = SystemAPI.HasComponent<Match.Team>(connection) ? SystemAPI.GetComponent<Match.Team>(connection).Value : Match.TeamRules.None;
                // A nuke planted in this team's base: the defenders come back far from it, not on their base pads
                // (owner, 28 Nov: defenders respawning beside the planted nuke disarmed every one).
                var plantedHere = mode == Match.MatchMode.CaptureTheNuke && placement.PlantedIn == team && team != Match.TeamRules.None;
                var pick = mode == Match.MatchMode.CaptureTheNuke && !plantedHere && placement.AtBase(ref position, ref rotation, team)
                    ? SpawnPick.Safe
                    : placement.Choose(ref position, ref rotation, team,
                        plantedHere ? placement.BaseOf(team) : float3.zero, plantedHere ? Match.NukeBaseSpawn.PlantedAwayDistance : 0f);
                if (pick == SpawnPick.None && placement.Points.Length > 0)
                    UnityEngine.Debug.LogWarning($"[Spawn] Player {id}: no spawn point can be used; the spawner's own point is used.");
                else if (pick == SpawnPick.Relaxed)
                    UnityEngine.Debug.Log($"[Spawn] Player {id}: every spawn point has an enemy near; the best of them is used.");

                var requested = SystemAPI.HasComponent<RequestedLoadout>(connection)
                    ? SystemAPI.GetComponent<RequestedLoadout>(connection)
                    : default;
                ChopperSpawning.Spawn(ecb, spawner, chassisPrefabs, connection, id, requested.Code, requested.SkinId, position, rotation,
                    isConnection: SystemAPI.HasComponent<NetworkId>(connection), team: team);
                ecb.RemoveComponent<RespawnTimer>(connection);
            }

            if (prepared)
            {
                // Remember which points were used (the reuse penalty of later spawns).
                for (var i = 0; i < placement.Points.Length; i++)
                    if (placement.Used[i]) SystemAPI.SetComponent(placement.Points[i], new SpawnPointUse { LastUsed = placement.Now });
                placement.Dispose();
            }

            ecb.Playback(state.EntityManager);
        }

        /// <summary>The map's spawn points measured against the level, and the live choppers.</summary>
        private SpawnPlacement Prepare(ref SystemState state, Entity spawnerEntity)
        {
            var rules = SystemAPI.HasComponent<SpawnRules>(spawnerEntity)
                ? SystemAPI.GetComponent<SpawnRules>(spawnerEntity)
                : SpawnRules.Default;
            var hasBounds = SystemAPI.TryGetSingleton<ArenaBounds>(out var bounds);
            var hasWorld = SystemAPI.TryGetSingleton<PhysicsWorldSingleton>(out var physics);
            // The physics world is written by jobs: wait for them before the queries.
            if (hasWorld) state.CompleteDependency();
            var now = SystemAPI.Time.ElapsedTime;

            var placement = new SpawnPlacement
            {
                Rules = rules,
                HasWorld = hasWorld,
                World = hasWorld ? physics.CollisionWorld : default,
                Now = now,
                Points = new NativeList<Entity>(16, Allocator.Temp),
                Places = new NativeList<SpawnPoint>(16, Allocator.Temp),
                Candidates = new NativeList<SpawnCandidate>(16, Allocator.Temp),
                Used = new NativeList<bool>(16, Allocator.Temp),
                Threats = new NativeList<SpawnThreat>(16, Allocator.Temp),
                Bases = new NativeList<Match.NukeBase>(2, Allocator.Temp),
                PadPosition = SystemAPI.TryGetSingleton<Match.NukePad>(out var pad) ? pad.Position : float3.zero,
                PlantedIn = SystemAPI.TryGetSingleton<Match.NukeState>(out var nuke) && nuke.Phase == Match.NukePhase.Planted
                    ? nuke.DockTeam
                    : Match.TeamRules.None,
            };
            foreach (var zone in SystemAPI.Query<RefRO<Match.NukeBase>>()) placement.Bases.Add(zone.ValueRO);

            foreach (var (point, use, entity) in SystemAPI.Query<RefRO<SpawnPoint>, RefRO<SpawnPointUse>>().WithEntityAccess())
            {
                var candidate = SpawnProbe.Level(point.ValueRO, hasWorld, placement.World, hasBounds, bounds, rules);
                candidate.SecondsSinceUse = (float)math.min(now - use.ValueRO.LastUsed, 1.0e6);
                placement.Points.Add(entity);
                placement.Places.Add(point.ValueRO);
                placement.Candidates.Add(candidate);
                placement.Used.Add(false);
            }

            // Enemy is set per spawning player in Choose (team mates only block the point itself).
            foreach (var (health, transform, chopper) in SystemAPI.Query<RefRO<Health>, RefRO<LocalTransform>>().WithAll<ConnectionOwner>().WithEntityAccess())
            {
                if (health.ValueRO.IsDead) continue;
                var team = SystemAPI.HasComponent<Match.Team>(chopper) ? SystemAPI.GetComponent<Match.Team>(chopper).Value : Match.TeamRules.None;
                placement.Threats.Add(new SpawnThreat { Position = transform.ValueRO.Position, Enemy = true, Team = team });
            }
            return placement;
        }

        /// <summary>One tick's spawn work: the points, what is known about them, and the choppers to keep away from.</summary>
        private struct SpawnPlacement
        {
            public SpawnRules Rules;
            public bool HasWorld;
            public CollisionWorld World;
            public double Now;
            public NativeList<Entity> Points;
            public NativeList<SpawnPoint> Places;
            public NativeList<SpawnCandidate> Candidates;
            public NativeList<bool> Used;
            public NativeList<SpawnThreat> Threats;
            /// <summary>Capture the Nuke: the base zones (team spawns) and the pad they face.</summary>
            public NativeList<Match.NukeBase> Bases;
            public float3 PadPosition;
            /// <summary>The team whose base holds a planted nuke (None when nothing is planted).</summary>
            public byte PlantedIn;

            /// <summary>The team's base centre (zero when it has none).</summary>
            public float3 BaseOf(byte team)
            {
                for (var b = 0; b < Bases.Length; b++)
                    if (Bases[b].Team == team) return Bases[b].Position;
                return float3.zero;
            }

            /// <summary>
            /// Capture the Nuke: the first free slot of the team's base grid (<see cref="Match.NukeBaseSpawn"/>). False when
            /// the team has no base (then the normal spawn points are used).
            /// </summary>
            public bool AtBase(ref float3 position, ref quaternion rotation, byte team)
            {
                for (var b = 0; b < Bases.Length; b++)
                {
                    if (Bases[b].Team != team) continue;
                    var home = Bases[b].Position;
                    var occupied = new NativeArray<float3>(Threats.Length, Allocator.Temp);
                    for (var i = 0; i < Threats.Length; i++) occupied[i] = Threats[i].Position;
                    var slot = Match.NukeBaseSpawn.FreeSlot(home, PadPosition - home, occupied);
                    occupied.Dispose();
                    Match.NukeBaseSpawn.Slot(home, PadPosition - home, slot, out position, out rotation);
                    Threats.Add(new SpawnThreat { Position = position, Enemy = true, Team = team });
                    return true;
                }
                return false;
            }

            /// <summary>
            /// Sets the place of the best point (no change when none can be used). The chosen point counts as a chopper
            /// for the next spawn of the same tick, so two players never get the same or a near point.
            /// </summary>
            public SpawnPick Choose(ref float3 position, ref quaternion rotation, byte team, float3 avoid = default, float avoidRadius = 0f)
            {
                for (var i = 0; i < Threats.Length; i++)
                {
                    var threat = Threats[i];
                    threat.Enemy = Match.TeamRules.AreEnemies(team, threat.Team);
                    Threats[i] = threat;
                }
                for (var i = 0; i < Candidates.Length; i++)
                {
                    var candidate = Candidates[i];
                    SpawnProbe.Threats(ref candidate, Places[i].Position, Threats.AsArray(), HasWorld, World, Rules);
                    Candidates[i] = candidate;
                }

                // Points too near <avoid> (a base with a planted nuke) count as out of bounds for this spawn only.
                var scored = new NativeArray<SpawnCandidate>(Candidates.AsArray(), Allocator.Temp);
                if (avoidRadius > 0f)
                    for (var i = 0; i < scored.Length; i++)
                        if (math.distance(Places[i].Position, avoid) < avoidRadius)
                        {
                            var far = scored[i];
                            far.InBounds = false;
                            scored[i] = far;
                        }
                var best = SpawnScoring.Pick(scored, Rules, out var pick);
                scored.Dispose();
                // No point far enough: the best of all points (a spawn always beats none).
                if (best < 0 && avoidRadius > 0f) return Choose(ref position, ref rotation, team);
                if (best < 0) return pick;

                position = Places[best].Position;
                rotation = Places[best].Rotation;
                Used[best] = true;
                var used = Candidates[best];
                used.SecondsSinceUse = 0f;
                Candidates[best] = used;
                Threats.Add(new SpawnThreat { Position = position, Enemy = true, Team = team });
                return pick;
            }

            public void Dispose()
            {
                Points.Dispose();
                Places.Dispose();
                Candidates.Dispose();
                Used.Dispose();
                Threats.Dispose();
                Bases.Dispose();
            }
        }
    }
}
