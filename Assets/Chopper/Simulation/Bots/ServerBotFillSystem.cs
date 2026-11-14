using Chopper.Simulation.Match;
using Chopper.Simulation.Session;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace Chopper.Simulation.Bots
{
    /// <summary>
    /// Server only. Keeps the number of bots right (<see cref="BotFill"/>): bots fill the match up to the target while
    /// at least one person plays; when a person joins, one bot leaves. One change per <see cref="BotFillRules.StepSeconds"/>.
    /// <para>A new bot is a player entity without a connection (<see cref="ServerBot"/>) with a respawn timer of 0 s, so
    /// <see cref="ChopperRespawnSystem"/> spawns its chopper like a player's. A bot that leaves is destroyed; its chopper
    /// goes with it (linked group) and <see cref="MatchScoreSystem"/> tells the clients (player left).</para>
    /// <para><c>-bots N</c> on the server command line: always N bots while a person plays (0 = none).</para>
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(ChopperRespawnSystem))]
    public partial struct ServerBotFillSystem : ISystem
    {
        private double _nextStep;
        /// <summary>-1 = no override.</summary>
        private int _forced;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ChopperSpawner>();
            state.RequireForUpdate<BotFillRules>();
            var forced = ServerArgs.Float("-bots", -1f);
            _forced = forced < 0f ? -1 : System.Math.Min((int)forced, BotSettings.HardMaxBots);
        }

        public void OnUpdate(ref SystemState state)
        {
            var now = SystemAPI.Time.ElapsedTime;
            if (now < _nextStep) return;

            var spawner = SystemAPI.GetSingletonEntity<ChopperSpawner>();
            var rules = SystemAPI.GetComponent<BotFillRules>(spawner);
            var humans = SystemAPI.QueryBuilder().WithAll<PlayerId, NetworkId>().Build().CalculateEntityCount();
            var wanted = _forced >= 0
                ? (humans > 0 ? _forced : 0)
                : BotFill.Wanted(humans, rules.TargetPlayers, rules.MaxBots);

            var bots = new NativeList<BotStanding>(8, Allocator.Temp);
            var entities = new NativeList<Entity>(8, Allocator.Temp);
            foreach (var (bot, entity) in SystemAPI.Query<RefRO<ServerBot>>().WithEntityAccess())
            {
                bots.Add(new BotStanding
                {
                    Slot = bot.ValueRO.Slot,
                    Kills = SystemAPI.HasComponent<PlayerScore>(entity) ? SystemAPI.GetComponent<PlayerScore>(entity).Kills : 0,
                    // A bot with a timer waits for its next chopper.
                    Alive = !SystemAPI.HasComponent<RespawnTimer>(entity),
                });
                entities.Add(entity);
            }

            if (bots.Length < wanted)
            {
                Add(ref state, spawner, BotFill.FreeSlot(bots.AsArray()));
                _nextStep = now + rules.StepSeconds;
            }
            else if (bots.Length > wanted)
            {
                var index = BotFill.PickToRemove(bots.AsArray());
                if (index >= 0)
                {
                    UnityEngine.Debug.Log($"[Bots] Bot {BotIds.Id(bots[index].Slot)} leaves ({humans} player(s), {wanted} bot(s) wanted).");
                    // The linked group takes the chopper with it.
                    state.EntityManager.DestroyEntity(entities[index]);
                }
                // With no people left, all bots go at once.
                _nextStep = humans > 0 ? now + rules.StepSeconds : now;
            }
            bots.Dispose();
            entities.Dispose();
        }

        private void Add(ref SystemState state, Entity spawner, int slot)
        {
            var id = BotIds.Id(slot);
            var loadout = default(Loadout.LoadoutCode);
            var hasMapHandling = SystemAPI.HasComponent<BotHandling>(spawner);
            var handling = hasMapHandling ? SystemAPI.GetComponent<BotHandling>(spawner) : default;
            var name = new FixedString32Bytes();
            var hasRow = false;
            if (SystemAPI.HasBuffer<BotRosterEntry>(spawner))
            {
                var roster = SystemAPI.GetBuffer<BotRosterEntry>(spawner);
                if (roster.Length > 0)
                {
                    var row = roster[slot % roster.Length];
                    loadout = row.Loadout;
                    name = row.Name;
                    if (row.HasHandling)
                    {
                        handling = row.Handling;
                        hasRow = true;
                    }
                }
            }
            if (!hasRow && !hasMapHandling)
            {
                UnityEngine.Debug.LogWarning("[Bots] No bot profile on the spawner or the roster row: no bot added. Press Chopper > Setup, step 4.");
                return;
            }

            var manager = state.EntityManager;
            var bot = manager.CreateEntity();
            manager.AddComponentData(bot, new ServerBot { Slot = slot, Kind = BotKind.Air });
            manager.AddComponentData(bot, new PlayerId { Value = id });
            manager.AddComponentData(bot, new BotSkill { Value = handling });
            manager.AddComponentData(bot, new RequestedLoadout { Code = loadout });
            manager.AddComponentData(bot, new RespawnTimer { Remaining = 0f });
            // The bot itself first: destroying the bot destroys the group (its chopper).
            manager.AddBuffer<LinkedEntityGroup>(bot).Add(new LinkedEntityGroup { Value = bot });
            BotWorld.AddBotParts(manager, bot, (uint)id * 7919u + 17u);
            UnityEngine.Debug.Log($"[Bots] Bot {id} ({name}) joins, chassis {loadout.ChassisId}.");
        }
    }
}
