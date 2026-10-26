using System.Collections.Generic;
using Chopper.Simulation.Combat;
using Chopper.Simulation.Definitions;
using Chopper.Simulation.Loadout;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Chopper.Simulation.Flight
{
    /// <summary>
    /// Put on the chopper ghost prefab together with a Rigidbody (useGravity on, low damping, Interpolate) and colliders.
    /// All tuning comes from assets: the chassis (flight feel, health, crash) and its default loadout (modules scale the
    /// stats; the nose and pylon modules give the weapons). The values go through <see cref="LoadoutRules.Compute"/>,
    /// the same rules the hangar and the server use. TODO: the server applies each player's own loadout at spawn.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ChopperAuthoring : MonoBehaviour
    {
        [SerializeField] private ChassisDefinition chassis;
        [SerializeField, Tooltip("Default loadout: one module per chassis slot, same order. Empty entries leave a slot empty.")]
        private ModuleDefinition[] modules = System.Array.Empty<ModuleDefinition>();

        public ChassisDefinition Chassis => chassis;

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetLoadout(ChassisDefinition chassisDefinition, ModuleDefinition[] defaultModules)
        {
            chassis = chassisDefinition;
            modules = defaultModules;
        }

        /// <summary>Catalog made of only the assets on this prefab, for <see cref="LoadoutRules"/>.</summary>
        private sealed class PrefabCatalog : ILoadoutCatalog, IPlayerUnlocks
        {
            private readonly ChassisSpec _chassis;
            private readonly Dictionary<ushort, ModuleSpec> _modules = new();

            public PrefabCatalog(ChassisDefinition chassis, IEnumerable<ModuleDefinition> modules)
            {
                _chassis = chassis.ToSpec();
                foreach (var module in modules)
                    if (module != null) _modules[module.Id] = module.ToSpec();
            }

            public bool TryGetChassis(ushort id, out ChassisSpec chassis)
            {
                chassis = _chassis;
                return id == _chassis.Id;
            }

            public bool TryGetModule(ushort id, out ModuleSpec module) => _modules.TryGetValue(id, out module);

            // The default loadout is not limited by level or ownership.
            public int Level => int.MaxValue;
            public bool Owns(ushort moduleId) => true;
        }

        private sealed class Baker : Baker<ChopperAuthoring>
        {
            private const float FallbackHealth = 100f;

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
                }

                var stats = ComputeStats(authoring, chassis, modules);
                var flight = chassis != null ? chassis.Flight : new FlightTuning();
                var crash = chassis != null ? chassis.Crash : new CrashTuning();

                AddComponent(entity, new ChopperFlightConfig
                {
                    GravityScale = flight.GravityScale,
                    CollectiveRange = flight.CollectiveRange * stats.LiftMultiplier,
                    TiltCompensation = flight.TiltCompensation,
                    MaxCompensatedTilt = math.cos(math.radians(flight.MaxCompensatedTiltDegrees)),
                    MaxTurnRate = math.radians(new float3(flight.MaxPitchRate, flight.MaxYawRate, flight.MaxRollRate)) * stats.TurnRateMultiplier,
                    TurnResponse = flight.TurnResponse,
                    QuadraticDrag = flight.QuadraticDrag,
                    LinearDrag = flight.LinearDrag,
                    Weathervane = flight.Weathervane,
                    AutoLevel = flight.AutoLevel,
                    DashBoost = flight.DashBoost,
                    DashCooldown = flight.DashCooldown * stats.DashCooldownMultiplier,
                });
                AddComponent<ChopperFlightState>(entity);
                AddComponent<ChopperInput>(entity);

                AddComponent(entity, new Health { Current = stats.Health, Max = stats.Health });
                AddBuffer<DamageEvent>(entity);
                AddComponent<LastAttacker>(entity);
                AddComponent(entity, new CrashDamageConfig
                {
                    SafeSpeedLoss = crash.SafeSpeedLoss,
                    DamagePerSpeedLoss = crash.DamagePerSpeedLoss,
                });
                AddComponent<CrashSensor>(entity);

                BakeWeapons(entity, modules);
            }

            private static LoadoutStats ComputeStats(ChopperAuthoring authoring, ChassisDefinition chassis, ModuleDefinition[] modules)
            {
                if (chassis == null)
                {
                    Debug.LogWarning($"{authoring.name}: no chassis set. Using default flight and {FallbackHealth} HP. " +
                                     "Run Chopper → Setup to assign one.", authoring);
                    return new LoadoutStats(FallbackHealth, 0f, 0f, 1f, 1f, 1f);
                }

                var ids = new ushort[chassis.Slots.Count];
                for (var i = 0; i < ids.Length && i < modules.Length; i++)
                    ids[i] = modules[i] != null ? modules[i].Id : LoadoutSelection.EmptySlot;

                var catalog = new PrefabCatalog(chassis, modules);
                var selection = new LoadoutSelection(chassis.Id, ids);
                var result = LoadoutRules.Validate(selection, catalog, catalog);
                if (!result.IsValid)
                    Debug.LogWarning($"{authoring.name}: default loadout is not valid ({result}).", authoring);

                return LoadoutRules.Compute(catalog.TryGetChassis(chassis.Id, out var spec) ? spec : chassis.ToSpec(), selection, catalog);
            }

            /// <summary>First nose module with a hitscan weapon is the gun; first pylon with rockets is the launcher.</summary>
            private void BakeWeapons(Entity entity, ModuleDefinition[] modules)
            {
                HitscanWeaponDefinition gun = null;
                RocketWeaponDefinition rockets = null;
                foreach (var module in modules)
                {
                    if (module == null) continue;
                    if (gun == null && module.Weapon is HitscanWeaponDefinition hitscan) gun = hitscan;
                    if (rockets == null && module.Weapon is RocketWeaponDefinition rocket) rockets = rocket;
                }

                if (gun != null)
                {
                    AddComponent(entity, new HitscanWeaponConfig
                    {
                        FireInterval = gun.FireInterval,
                        Damage = gun.DamagePerShot,
                        Range = gun.Range,
                        MuzzleOffset = gun.MuzzleOffset,
                    });
                    AddComponent<HitscanWeaponState>(entity);
                }

                if (rockets != null)
                {
                    AddComponent(entity, new RocketLauncherConfig
                    {
                        FireInterval = rockets.FireInterval,
                        Speed = rockets.Speed,
                        Lifetime = rockets.Lifetime,
                        DirectDamage = rockets.DirectDamage,
                        SplashDamage = rockets.SplashDamage,
                        SplashRadius = rockets.SplashRadius,
                        MuzzleOffset = rockets.MuzzleOffset,
                    });
                    AddComponent<RocketLauncherState>(entity);
                }
            }
        }
    }
}
