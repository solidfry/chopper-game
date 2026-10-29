using System.Collections.Generic;
using Chopper.Simulation.Combat;
using Chopper.Simulation.Flight;
using Chopper.Simulation.Session;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace Chopper.Simulation.Loadout
{
    /// <summary>
    /// Client and server. When a chopper's <see cref="ChopperLoadout"/> differs from what this world applied, builds it
    /// from the catalog (<see cref="LoadoutBuilder"/>, same code as the baker) and writes flight, health, crash, gun,
    /// pylons and flares. The server validates first (<see cref="LoadoutRules.Validate"/>) and falls back to the
    /// chassis default; the corrected loadout then reaches every client through the ghost field.
    /// Managed on purpose: it reuses the loadout rules and assets instead of a second, Burst copy of them.
    /// TODO: player unlocks from Cloud Save (everything is unlocked for now).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation)]
    // With the first systems: after ghosts spawn (GhostSimulationSystemGroup), before prediction, so a new chopper flies
    // with its loadout from its first predicted tick. (Prediction is OrderFirst: a plain UpdateBefore is ignored.)
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    [UpdateAfter(typeof(GhostSimulationSystemGroup))]
    [UpdateBefore(typeof(PredictedSimulationSystemGroup))]
    public partial class LoadoutApplySystem : SystemBase
    {
        private readonly Dictionary<ushort, ChassisDefinition> _chassis = new();
        private readonly Dictionary<ushort, ModuleDefinition> _modules = new();
        private LoadoutCatalog _indexed;
        private EntityQuery _changed;

        protected override void OnCreate()
        {
            RequireForUpdate<LoadoutCatalogRef>();
            RequireForUpdate<ChopperSpawner>();
            _changed = GetEntityQuery(ComponentType.ReadOnly<ChopperLoadout>(), ComponentType.ReadWrite<AppliedLoadout>());
        }

        protected override void OnUpdate()
        {
            var catalog = SystemAPI.GetSingleton<LoadoutCatalogRef>().Catalog.Value;
            if (catalog == null) return;
            Index(catalog);

            var missilePrefab = SystemAPI.GetSingleton<ChopperSpawner>().MissilePrefab;
            var isServer = World.IsServer();

            // Collect first: writing buffers while iterating a query is not allowed.
            var entities = _changed.ToEntityArray(Allocator.Temp);
            foreach (var entity in entities)
            {
                var wanted = EntityManager.GetComponentData<ChopperLoadout>(entity).Code;
                var applied = EntityManager.GetComponentData<AppliedLoadout>(entity).Code;
                if (wanted.Equals(applied)) continue;
                Apply(entity, wanted, missilePrefab, isServer);
            }
            entities.Dispose();
        }

        private void Apply(Entity entity, LoadoutCode wanted, Entity missilePrefab, bool isServer)
        {
            var sockets = new List<ChassisSocket>();
            if (EntityManager.HasBuffer<ChassisSocket>(entity))
                foreach (var socket in EntityManager.GetBuffer<ChassisSocket>(entity)) sockets.Add(socket);

            if (!_chassis.TryGetValue(wanted.ChassisId, out var chassis))
            {
                Debug.LogWarning($"[{World.Name}] Loadout asks for unknown chassis {wanted.ChassisId}; keeping the current one.");
                if (isServer) EntityManager.SetComponentData(entity, ChopperLoadout.From(EntityManager.GetComponentData<AppliedLoadout>(entity).Code));
                return;
            }

            var built = LoadoutBuilder.Build(chassis, ModulesFor(chassis, wanted), sockets, _ => missilePrefab);
            if (isServer && !built.Validation.IsValid)
            {
                Debug.LogWarning($"[Server] Loadout not valid ({built.Validation}); using the {chassis.DisplayName} default.");
                built = LoadoutBuilder.Build(chassis, chassis.DefaultModules, sockets, _ => missilePrefab);
                EntityManager.SetComponentData(entity, ChopperLoadout.From(built.Code));
            }

            Write(entity, built, isServer);
            EntityManager.SetComponentData(entity, new AppliedLoadout { Code = isServer ? built.Code : wanted });
        }

        private ModuleDefinition[] ModulesFor(ChassisDefinition chassis, in LoadoutCode code)
        {
            var modules = new ModuleDefinition[chassis.Slots.Count];
            for (var slot = 0; slot < modules.Length; slot++)
                modules[slot] = _modules.TryGetValue(code.GetModule(slot), out var module) ? module : null;
            return modules;
        }

        private void Write(Entity entity, BuiltLoadout built, bool isServer)
        {
            EntityManager.SetComponentData(entity, built.Flight);
            EntityManager.SetComponentData(entity, built.Crash);
            EntityManager.SetComponentData(entity, built.Gun);
            EntityManager.SetComponentData(entity, built.Countermeasure);

            // Max is not sent; the server also starts the new chopper at full health.
            var health = EntityManager.GetComponentData<Health>(entity);
            health.Max = built.Health;
            if (isServer) health.Current = built.Health;
            EntityManager.SetComponentData(entity, health);

            var pylons = EntityManager.GetBuffer<PylonWeapon>(entity);
            pylons.Clear();
            foreach (var pylon in built.Pylons) pylons.Add(pylon);
            var cooldowns = EntityManager.GetBuffer<PylonWeaponState>(entity);
            cooldowns.ResizeUninitialized(built.Pylons.Count);
            for (var i = 0; i < cooldowns.Length; i++) cooldowns[i] = default;

            var selection = EntityManager.GetComponentData<PylonSelection>(entity);
            if (selection.Selected >= built.Pylons.Count) EntityManager.SetComponentData(entity, new PylonSelection());
        }

        private void Index(LoadoutCatalog catalog)
        {
            if (_indexed == catalog) return;
            _indexed = catalog;
            _chassis.Clear();
            _modules.Clear();
            foreach (var chassis in catalog.Chassis) if (chassis != null) _chassis[chassis.Id] = chassis;
            foreach (var module in catalog.Modules) if (module != null) _modules[module.Id] = module;
        }
    }
}
