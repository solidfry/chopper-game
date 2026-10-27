using System.Collections.Generic;
using Chopper.Simulation.Combat;
using Chopper.Simulation.Loadout;
using Unity.Entities;
using UnityEngine;

namespace Chopper.Simulation.Flight
{
    /// <summary>
    /// Put on the chopper ghost prefab together with a Rigidbody (useGravity on, low damping, Interpolate) and colliders.
    /// All tuning comes from assets: the chassis (flight feel, health, crash) and its default loadout, built by
    /// <see cref="LoadoutBuilder"/> (the same code applies each player's own loadout at runtime). Module launch points
    /// come from <see cref="SocketAuthoring"/> children.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ChopperAuthoring : MonoBehaviour
    {
        [SerializeField] private ChassisDefinition chassis;
        [SerializeField, Tooltip("Default loadout: one module per chassis slot, same order. Empty entries leave a slot empty.")]
        private ModuleDefinition[] modules = System.Array.Empty<ModuleDefinition>();

        [Header("Network smoothing (own chopper)")]
        [SerializeField, Tooltip("Server corrections larger than this (metres) snap at once (respawn, big desync).")]
        private float smoothingMaxDistance = PredictionSmoothing.Default.MaxDistance;
        [SerializeField, Range(0.05f, 1f), Tooltip("Part of a position correction applied per server update. 1 = snap, lower = softer but drifts longer.")]
        private float smoothingPosition = PredictionSmoothing.Default.PositionFraction;
        [SerializeField, Range(0.05f, 1f), Tooltip("Part of a rotation correction applied per server update.")]
        private float smoothingRotation = PredictionSmoothing.Default.RotationFraction;

        public ChassisDefinition Chassis => chassis;
        public IReadOnlyList<ModuleDefinition> Modules => modules;

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetLoadout(ChassisDefinition chassisDefinition, ModuleDefinition[] defaultModules)
        {
            chassis = chassisDefinition;
            modules = defaultModules;
        }

        private sealed class Baker : Baker<ChopperAuthoring>
        {
            public override void Bake(ChopperAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                var chassis = authoring.chassis;
                var modules = authoring.modules ?? System.Array.Empty<ModuleDefinition>();

                // Re-bake when any of the assets change.
                if (chassis != null) DependsOn(chassis);
                foreach (var module in modules)
                {
                    if (module == null) continue;
                    DependsOn(module);
                    if (module.Weapon != null) DependsOn(module.Weapon);
                    if (module.Ability != null) DependsOn(module.Ability);
                }

                var sockets = BakeSockets(authoring);
                var built = LoadoutBuilder.Build(chassis, modules, sockets, prefab => GetEntity(prefab, TransformUsageFlags.Dynamic));
                if (chassis == null)
                    Debug.LogWarning($"{authoring.name}: no chassis set. Using default flight. Run Chopper → Setup to assign one.", authoring);
                else if (!built.Validation.IsValid)
                    Debug.LogWarning($"{authoring.name}: default loadout is not valid ({built.Validation}).", authoring);

                AddComponent(entity, built.Flight);
                AddComponent<ChopperFlightState>(entity);
                AddComponent<ChopperInput>(entity);
                AddComponent(entity, new PredictionSmoothing
                {
                    MaxDistance = authoring.smoothingMaxDistance,
                    PositionFraction = authoring.smoothingPosition,
                    RotationFraction = authoring.smoothingRotation,
                });

                AddComponent(entity, new Health { Current = built.Health, Max = built.Health });
                AddBuffer<DamageEvent>(entity);
                AddComponent<LastAttacker>(entity);
                AddComponent(entity, built.Crash);
                AddComponent<CrashSensor>(entity);

                // Every chopper has every weapon component: a ghost's component set is fixed, and loadouts change at
                // runtime. A missing gun or flares is just a zero config.
                AddComponent(entity, built.Gun);
                AddComponent<HitscanWeaponState>(entity);
                AddComponent(entity, built.Countermeasure);
                AddComponent<CountermeasureState>(entity);

                var pylons = AddBuffer<PylonWeapon>(entity);
                foreach (var pylon in built.Pylons) pylons.Add(pylon);
                var cooldowns = AddBuffer<PylonWeaponState>(entity);
                for (var i = 0; i < built.Pylons.Count; i++) cooldowns.Add(default);
                AddComponent<PylonSelection>(entity);
                AddComponent<TargetLock>(entity);
                AddComponent<ThreatState>(entity);
                AddComponent(entity, new Targetable { Class = TargetClass.Air, BaseHeat = 1f });

                AddComponent(entity, ChopperLoadout.From(built.Code));
                AddComponent(entity, new AppliedLoadout { Code = built.Code });
                var socketBuffer = AddBuffer<ChassisSocket>(entity);
                foreach (var socket in sockets) socketBuffer.Add(socket);
            }

            /// <summary>Sockets on children, relative to the chopper root.</summary>
            private List<ChassisSocket> BakeSockets(ChopperAuthoring authoring)
            {
                var root = authoring.transform;
                var sockets = new List<ChassisSocket>();
                foreach (var socket in GetComponentsInChildren<SocketAuthoring>())
                {
                    var transform = socket.transform;
                    DependsOn(transform);
                    sockets.Add(new ChassisSocket
                    {
                        Slot = socket.Slot,
                        Index = (byte)Mathf.Clamp(socket.Index, 0, 255),
                        LocalPosition = root.InverseTransformPoint(transform.position),
                        LocalRotation = Quaternion.Inverse(root.rotation) * transform.rotation,
                    });
                }
                return sockets;
            }
        }
    }
}
