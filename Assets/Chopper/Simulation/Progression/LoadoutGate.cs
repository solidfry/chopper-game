using System;
using Chopper.Simulation.Loadout;
using Chopper.Simulation.Session;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Chopper.Simulation.Progression
{
    /// <summary>
    /// Client world singleton fields are on <see cref="LoadoutChoice"/>; this is the server side. On a connection: the
    /// XP the player has on the three tracks, for the loadout it asked for (account, the chassis class, the pilot).
    /// <see cref="Trusted"/> false = the client sent the numbers (a player can edit a local save): good enough to
    /// test locks, not for ranked play. Cloud Save will write it with Trusted true (plan section 6a-1, step 4).
    /// A server bot has no such component: nothing is locked for it.
    /// </summary>
    public struct PlayerProgress : IComponentData
    {
        public int AccountXp;
        public int ChassisXp;
        public int PilotXp;
        public bool Trusted;
    }

    /// <summary>Server, on a connection: <see cref="LoadoutGateSystem"/> has checked its requested loadout.</summary>
    public struct LoadoutGated : IComponentData
    {
    }

    /// <summary>Baked on the spawner from <see cref="ProgressionSettings"/>: the level curves and the lock switch.</summary>
    public struct ProgressionRules : IComponentData
    {
        public LevelCurve Account;
        public LevelCurve Chassis;
        public LevelCurve Pilot;
        public bool LocksOn;
    }

    /// <summary>Baked on the spawner: the account level a paint skin needs (the skin library is not in the server's assembly).</summary>
    [InternalBufferCapacity(0)]
    public struct SkinUnlock : IBufferElementData
    {
        public ushort SkinId;
        public int Level;
    }

    /// <summary>One row of the spawner's skin unlock list (set by Setup step 4 from the skin library).</summary>
    [Serializable]
    public struct SkinUnlockRow
    {
        public ushort SkinId;
        public int Level;
    }

    /// <summary>Levels worked out from a <see cref="PlayerProgress"/>. The chassis level is for the chassis class asked for.</summary>
    public sealed class ProgressUnlocks : IPlayerUnlocks
    {
        private readonly int _chassisLevel;
        private readonly int _pilotLevel;

        public ProgressUnlocks(int accountLevel, int chassisLevel, int pilotLevel)
        {
            Level = accountLevel;
            _chassisLevel = chassisLevel;
            _pilotLevel = pilotLevel;
        }

        public ProgressUnlocks(in PlayerProgress progress, in ProgressionRules rules)
            : this(rules.Account.Level(progress.AccountXp), rules.Chassis.Level(progress.ChassisXp), rules.Pilot.Level(progress.PilotXp))
        {
        }

        public int Level { get; }
        /// <summary>No purchases yet: every module is owned.</summary>
        public bool Owns(ushort moduleId) => true;
        public int ChassisLevel(ChassisClass chassisClass) => _chassisLevel;
        public int PilotLevel(ushort pilotId) => _pilotLevel;
    }

    /// <summary>What <see cref="LoadoutGate.Apply"/> took away.</summary>
    [Flags]
    public enum GateChange : byte
    {
        None = 0,
        /// <summary>The chassis is locked: the default chopper is used.</summary>
        Chassis = 1,
        /// <summary>A module is locked: the chassis default modules are used.</summary>
        Modules = 2,
        Pilot = 4,
        Skin = 8,
    }

    /// <summary>The server's unlock check for a requested loadout. Pure (edit-mode tests: ProgressionTests).</summary>
    public static class LoadoutGate
    {
        /// <summary>
        /// Takes away what the player has not unlocked. A locked chassis: the code becomes empty (the default chopper).
        /// A locked module: <paramref name="chassisDefault"/> (the pilot is kept). A locked pilot or skin: none.
        /// Other faults (unknown ids, wrong slots, weight) are left for <see cref="LoadoutApplySystem"/>.
        /// </summary>
        /// <param name="pilotLevelNeeded">Account level of the pilot asked for (0 = no lock, or no pilot).</param>
        /// <param name="skinLevelNeeded">Account level of the skin asked for (0 = no lock, or no skin).</param>
        public static GateChange Apply(ref LoadoutCode code, ref ushort skinId, ILoadoutCatalog catalog, IPlayerUnlocks unlocks,
            int pilotLevelNeeded, int skinLevelNeeded, in LoadoutCode chassisDefault)
        {
            var change = GateChange.None;
            if (skinId != 0 && unlocks.Level < skinLevelNeeded)
            {
                skinId = 0;
                change |= GateChange.Skin;
            }
            if (code.IsEmpty || !catalog.TryGetChassis(code.ChassisId, out var chassis)) return change;

            var result = LoadoutRules.Validate(code.ToSelection(chassis.Slots.Count), catalog, unlocks);
            if (result.Error == LoadoutError.ChassisLocked)
            {
                code = default;
                return change | GateChange.Chassis;
            }
            if (result.Error is LoadoutError.ModuleLocked or LoadoutError.MasteryLocked or LoadoutError.NotOwned)
            {
                var pilot = code.PilotId;
                code = chassisDefault;
                code.PilotId = pilot;
                change |= GateChange.Modules;
            }
            if (code.PilotId != 0 && unlocks.Level < pilotLevelNeeded)
            {
                code.PilotId = 0;
                change |= GateChange.Pilot;
            }
            return change;
        }
    }

    /// <summary>
    /// Server: checks each new connection's requested loadout against its progress, once, before the first spawn
    /// (<see cref="ChopperRespawnSystem"/> spawns what is left). Does nothing but mark the connection when the map has
    /// no <see cref="ProgressionRules"/> or the locks are off. Managed on purpose: it reuses the catalog and
    /// <see cref="LoadoutRules"/>. Written without the editor (13 Nov 2026).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(GoInGameServerSystem))]
    [UpdateBefore(typeof(ChopperRespawnSystem))]
    public partial class LoadoutGateSystem : SystemBase
    {
        private EntityQuery _new;

        protected override void OnCreate()
        {
            _new = GetEntityQuery(ComponentType.ReadOnly<PlayerProgress>(), ComponentType.ReadWrite<RequestedLoadout>(),
                ComponentType.Exclude<LoadoutGated>());
            RequireForUpdate(_new);
            RequireForUpdate<LoadoutCatalogRef>();
        }

        protected override void OnUpdate()
        {
            var catalog = SystemAPI.GetSingleton<LoadoutCatalogRef>().Catalog.Value;
            var locksOn = SystemAPI.TryGetSingleton<ProgressionRules>(out var rules) && rules.LocksOn && catalog != null;
            var skins = locksOn && SystemAPI.TryGetSingletonBuffer<SkinUnlock>(out var skinBuffer, true)
                ? skinBuffer.ToNativeArray(Allocator.Temp)
                : default;

            var connections = _new.ToEntityArray(Allocator.Temp);
            foreach (var connection in connections)
            {
                if (locksOn) Check(connection, catalog, rules, skins);
                EntityManager.AddComponent<LoadoutGated>(connection);
            }
            connections.Dispose();
            if (skins.IsCreated) skins.Dispose();
        }

        private void Check(Entity connection, LoadoutCatalog catalog, in ProgressionRules rules, NativeArray<SkinUnlock> skins)
        {
            var progress = EntityManager.GetComponentData<PlayerProgress>(connection);
            var requested = EntityManager.GetComponentData<RequestedLoadout>(connection);
            var code = requested.Code;
            var skinId = requested.SkinId;

            var pilot = catalog.FindPilot(code.PilotId);
            var skinLevel = 0;
            if (skins.IsCreated)
                foreach (var skin in skins)
                    if (skin.SkinId == skinId) skinLevel = skin.Level;

            var change = LoadoutGate.Apply(ref code, ref skinId, catalog, new ProgressUnlocks(progress, rules),
                pilot != null ? pilot.UnlockLevel : 0, skinLevel, DefaultFor(catalog, requested.Code.ChassisId));
            if (change == GateChange.None) return;

            EntityManager.SetComponentData(connection, new RequestedLoadout { Code = code, SkinId = skinId });
            var id = EntityManager.HasComponent<PlayerId>(connection) ? EntityManager.GetComponentData<PlayerId>(connection).Value : 0;
            Debug.LogWarning($"[Progress] Player {id}: locked items removed from the loadout ({change}). " +
                             $"Progress is {(progress.Trusted ? "trusted" : "the client's own claim")}.");
        }

        /// <summary>The chassis with its default modules (empty when the chassis is not in the catalog).</summary>
        private static LoadoutCode DefaultFor(LoadoutCatalog catalog, ushort chassisId)
        {
            foreach (var chassis in catalog.Chassis)
            {
                if (chassis == null || chassis.Id != chassisId) continue;
                var code = new LoadoutCode { ChassisId = chassisId };
                var modules = chassis.DefaultModules;
                for (var slot = 0; modules != null && slot < modules.Count && slot < LoadoutCode.MaxSlots; slot++)
                    code.SetModule(slot, modules[slot] != null ? modules[slot].Id : LoadoutSelection.EmptySlot);
                return code;
            }
            return default;
        }
    }
}
