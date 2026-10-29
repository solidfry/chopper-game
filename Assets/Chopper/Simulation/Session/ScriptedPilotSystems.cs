using Chopper.Simulation.Flight;
using Chopper.Simulation.Loadout;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Chopper.Simulation.Session
{
    /// <summary>
    /// A fixed flight pattern for test players: climb, then weave and turn with boost, gun and rocket bursts. Open loop
    /// (it does not look at the chopper), so it works on thin clients, which have no ghosts. Later bots can replace it
    /// with real decisions on the same input.
    /// </summary>
    public static class ScriptedPilot
    {
        public const float CycleSeconds = 24f;

        /// <param name="time">Seconds since the pilot started; give each player an offset so they do not fly in step.</param>
        public static ChopperInput Fly(float time)
        {
            var phase = time % CycleSeconds;
            var climbing = phase < 4f;
            return new ChopperInput
            {
                Thrust = climbing ? 1f : 0.15f + 0.35f * math.sin(time * 0.7f),
                // Mostly forward and a steady turn, so the pilot circles near where it started.
                Pitch = climbing ? 0f : 0.15f + 0.15f * math.sin(time * 0.5f),
                Roll = 0.4f * math.sin(time * 0.6f),
                Yaw = 0.35f + 0.25f * math.sin(time * 0.23f),
                Boost = phase >= 12f && phase < 14f,
                Fire = (phase >= 6f && phase < 9f) || (phase >= 16f && phase < 18f),
                FireSecondary = phase >= 9f && phase < 10f,
            };
        }
    }

    /// <summary>
    /// Dev switch: while this singleton is in the client world, the scripted pilot flies the local chopper (network and
    /// feel tests without a person at the controls).
    /// </summary>
    public struct ScriptedPilotOverride : IComponentData
    {
    }

    /// <summary>
    /// Thin clients (Multiplayer → Play Mode Tools → Num Thin Clients) are test players with no rendering and no ghosts.
    /// Each gets the next chassis in the catalog with its default modules, so a load test has every chassis. Runs before
    /// <see cref="GoInGameClientSystem"/>, which sends the choice.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ThinClientSimulation)]
    [UpdateBefore(typeof(GoInGameClientSystem))]
    public partial class ThinClientLoadoutSystem : SystemBase
    {
        protected override void OnCreate() => RequireForUpdate<LoadoutCatalogRef>();

        protected override void OnUpdate()
        {
            Enabled = false;
            if (SystemAPI.HasSingleton<LoadoutChoice>()) return;
            var catalog = SystemAPI.GetSingleton<LoadoutCatalogRef>().Catalog.Value;
            if (catalog == null || catalog.Chassis.Count == 0) return;

            var chassis = catalog.Chassis[(int)(World.SequenceNumber % (ulong)catalog.Chassis.Count)];
            if (chassis == null) return;
            var modules = new ushort[chassis.Slots.Count];
            for (var i = 0; i < modules.Length && i < chassis.DefaultModules.Count; i++)
                modules[i] = chassis.DefaultModules[i] != null ? chassis.DefaultModules[i].Id : LoadoutSelection.EmptySlot;

            EntityManager.CreateSingleton(new LoadoutChoice { Code = LoadoutCode.From(new LoadoutSelection(chassis.Id, modules)) });
        }
    }

    /// <summary>
    /// Writes <see cref="ScriptedPilot"/> input. Thin client: makes its input entity (a thin client has no chopper
    /// ghost, so the connection's command target is a plain entity with the input buffer) and flies it. Full client:
    /// only with <see cref="ScriptedPilotOverride"/> (<see cref="ChopperInputSystem"/> then leaves the input alone).
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    [UpdateBefore(typeof(CopyInputToCommandBufferSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ThinClientSimulation)]
    public partial struct ScriptedPilotSystem : ISystem
    {
        private double _start;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();
            _start = -1d;
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var thin = state.WorldUnmanaged.IsThinClient();
            if (!thin && !SystemAPI.HasSingleton<ScriptedPilotOverride>()) return;

            var connection = SystemAPI.GetSingletonEntity<NetworkStreamInGame>();
            var networkId = SystemAPI.GetComponent<NetworkId>(connection).Value;
            if (_start < 0d) _start = SystemAPI.Time.ElapsedTime;
            var input = ScriptedPilot.Fly((float)(SystemAPI.Time.ElapsedTime - _start) + networkId * 7.3f);

            if (thin && SystemAPI.QueryBuilder().WithAll<ChopperInput>().Build().IsEmpty)
            {
                var entity = state.EntityManager.CreateEntity();
                state.EntityManager.AddComponent<ChopperInput>(entity);
                state.EntityManager.AddBuffer<InputBufferData<ChopperInput>>(entity);
                state.EntityManager.AddComponentData(entity, new GhostOwner { NetworkId = networkId });
                state.EntityManager.SetComponentData(connection, new CommandTarget { targetEntity = entity });
                return;
            }

            // Full client: the local chopper. Thin client: its input entity (also owned by this connection).
            foreach (var (target, owner) in SystemAPI.Query<RefRW<ChopperInput>, RefRO<GhostOwner>>())
                if (owner.ValueRO.NetworkId == networkId) target.ValueRW = input;
        }
    }
}
