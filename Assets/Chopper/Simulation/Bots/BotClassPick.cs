using Chopper.Simulation.Loadout;
using Chopper.Simulation.Session;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace Chopper.Simulation.Bots
{
    /// <summary>On the spawner: one ready-made build a bot can fly (every chassis preset, <see cref="ChassisPreset"/>).</summary>
    [InternalBufferCapacity(0)]
    public struct BotLoadoutOption : IBufferElementData
    {
        public ChassisClass Class;
        public LoadoutCode Code;
    }

    /// <summary>On the spawner: the Capture the Nuke class mix, one row per class (<see cref="BotSettings"/>).</summary>
    [InternalBufferCapacity(0)]
    public struct BotClassShare : IBufferElementData
    {
        public ChassisClass Class;
        /// <summary>Share of the team: a weight of 2 wants twice as many of this class as a weight of 1.</summary>
        public byte Weight;
    }

    /// <summary>
    /// Server only, on a new bot in Capture the Nuke: its loadout is picked once it has a team (<see cref="BotClassPickSystem"/>).
    /// It does not spawn until then.
    /// </summary>
    public struct BotClassPick : IComponentData
    {
    }

    /// <summary>The class mix rules (owner, 28 Nov 2026: "bots in ctn should have class requirements"). Pure.</summary>
    public static class BotClassMix
    {
        /// <summary>
        /// The class the team is shortest of: the lowest count per weight. A tie goes to the higher weight, then to the
        /// earlier row. None when no share has a weight.
        /// </summary>
        public static ChassisClass Pick(in NativeArray<BotClassShare> shares, in NativeArray<int> countsByClass)
        {
            var best = ChassisClass.None;
            var bestRatio = float.MaxValue;
            var bestWeight = 0;
            for (var i = 0; i < shares.Length; i++)
            {
                var share = shares[i];
                if (share.Weight == 0) continue;
                var index = (int)share.Class;
                var count = index < countsByClass.Length ? countsByClass[index] : 0;
                var ratio = count / (float)share.Weight;
                if (ratio < bestRatio - 1e-4f || (ratio < bestRatio + 1e-4f && share.Weight > bestWeight))
                {
                    best = share.Class;
                    bestRatio = ratio;
                    bestWeight = share.Weight;
                }
            }
            return best;
        }

        /// <summary>The class of a chassis id, from the options (None when no option uses that chassis).</summary>
        public static ChassisClass ClassOf(in NativeArray<BotLoadoutOption> options, ushort chassisId)
        {
            for (var i = 0; i < options.Length; i++)
                if (options[i].Code.ChassisId == chassisId) return options[i].Class;
            return ChassisClass.None;
        }

        /// <summary>A build of <paramref name="wanted"/> class picked by <paramref name="roll"/> (0..1); false when there is none.</summary>
        public static bool PickOption(in NativeArray<BotLoadoutOption> options, ChassisClass wanted, float roll, out LoadoutCode code)
        {
            code = default;
            var count = 0;
            for (var i = 0; i < options.Length; i++)
                if (options[i].Class == wanted) count++;
            if (count == 0) return false;
            var pick = Unity.Mathematics.math.min((int)(roll * count), count - 1);
            for (var i = 0; i < options.Length; i++)
            {
                if (options[i].Class != wanted) continue;
                if (pick-- == 0)
                {
                    code = options[i].Code;
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Gives each new Capture the Nuke bot the class its team is shortest of (people and bots counted, by their chassis),
    /// then a random preset of that class. Runs once the bot has a team; the bot spawns after.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(ChopperRespawnSystem))]
    public partial struct BotClassPickSystem : ISystem
    {
        public void OnCreate(ref SystemState state) => state.RequireForUpdate<BotClassPick>();

        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingletonEntity<BotFillRules>(out var spawner) ||
                !SystemAPI.HasBuffer<BotLoadoutOption>(spawner) || !SystemAPI.HasBuffer<BotClassShare>(spawner))
            {
                // Nothing to pick from: keep the roster loadout.
                var plain = new EntityCommandBuffer(Allocator.Temp);
                foreach (var (_, entity) in SystemAPI.Query<RefRO<BotClassPick>>().WithEntityAccess()) plain.RemoveComponent<BotClassPick>(entity);
                plain.Playback(state.EntityManager);
                return;
            }
            var options = SystemAPI.GetBuffer<BotLoadoutOption>(spawner).ToNativeArray(Allocator.Temp);
            var shares = SystemAPI.GetBuffer<BotClassShare>(spawner).ToNativeArray(Allocator.Temp);
            // One bot per frame, so the next one counts this one's class.
            var pickEntity = Entity.Null;
            byte pickTeam = 0;
            var pickSlot = 0;
            foreach (var (team, bot, entity) in SystemAPI.Query<RefRO<Match.Team>, RefRO<ServerBot>>().WithAll<BotClassPick>().WithEntityAccess())
            {
                pickEntity = entity;
                pickTeam = team.ValueRO.Value;
                pickSlot = bot.ValueRO.Slot;
                break;
            }
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            if (pickEntity != Entity.Null)
            {
                // Classes already on this team (people and bots that have their loadout).
                var counts = new NativeArray<int>(8, Allocator.Temp);
                foreach (var (otherTeam, otherLoadout) in
                         SystemAPI.Query<RefRO<Match.Team>, RefRO<RequestedLoadout>>().WithAll<PlayerId>().WithNone<BotClassPick>())
                {
                    if (otherTeam.ValueRO.Value != pickTeam) continue;
                    var cls = (int)BotClassMix.ClassOf(options, otherLoadout.ValueRO.Code.ChassisId);
                    if (cls > 0 && cls < counts.Length) counts[cls]++;
                }
                var wanted = BotClassMix.Pick(shares, counts);
                counts.Dispose();

                var random = Unity.Mathematics.Random.CreateFromIndex((uint)(pickSlot * 7919 + 101));
                var requested = SystemAPI.GetComponent<RequestedLoadout>(pickEntity);
                if (wanted != ChassisClass.None && BotClassMix.PickOption(options, wanted, random.NextFloat(), out var code))
                {
                    code.PilotId = requested.Code.PilotId;
                    requested.Code = code;
                    SystemAPI.SetComponent(pickEntity, requested);
                    UnityEngine.Debug.Log($"[Bots] Bot {BotIds.Id(pickSlot)} (team {pickTeam}) flies a {wanted}, chassis {code.ChassisId}.");
                }
                ecb.RemoveComponent<BotClassPick>(pickEntity);
            }
            ecb.Playback(state.EntityManager);
            options.Dispose();
            shares.Dispose();
        }
    }
}
