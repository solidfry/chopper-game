using System;
using System.Collections.Generic;
using Chopper.Simulation.Combat;
using Chopper.Simulation.Definitions;
using Chopper.Simulation.Flight;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Chopper.Simulation.Loadout
{
    /// <summary>All chopper components a loadout gives, before they are written to an entity.</summary>
    public sealed class BuiltLoadout
    {
        public LoadoutCode Code;
        public LoadoutResult Validation;
        public ChopperFlightConfig Flight;
        public float Health;
        public CrashDamageConfig Crash;
        /// <summary>FireInterval 0 = no gun.</summary>
        public HitscanWeaponConfig Gun;
        public readonly List<PylonWeapon> Pylons = new();
        /// <summary>Cooldown 0 = no flares.</summary>
        public CountermeasureConfig Countermeasure;
    }

    /// <summary>
    /// Turns a chassis + modules into chopper components. One code path for the prefab baker (default loadout) and
    /// for runtime (each player's loadout, <see cref="LoadoutApplySystem"/>), so both always agree. Stats come from
    /// <see cref="LoadoutRules.Compute"/>; weapon launch points come from the chassis sockets.
    /// </summary>
    public static class LoadoutBuilder
    {
        private const float FallbackHealth = 100f;

        /// <param name="modulesBySlot">One module (or null) per chassis slot, in slot order.</param>
        /// <param name="resolveMissilePrefab">Missile ghost prefab → entity (baker: GetEntity; runtime: the shared prefab).</param>
        public static BuiltLoadout Build(ChassisDefinition chassis, IReadOnlyList<ModuleDefinition> modulesBySlot,
            IReadOnlyList<ChassisSocket> sockets, Func<GameObject, Entity> resolveMissilePrefab)
        {
            var built = new BuiltLoadout();
            var stats = Stats(chassis, modulesBySlot, built);
            var flight = chassis != null ? chassis.Flight : new FlightTuning();
            var crash = chassis != null ? chassis.Crash : new CrashTuning();

            built.Flight = new ChopperFlightConfig
            {
                GravityScale = flight.GravityScale,
                CollectiveRange = flight.CollectiveRange * stats.LiftMultiplier,
                TiltCompensation = flight.TiltCompensation,
                MaxCompensatedTilt = math.cos(math.radians(flight.MaxCompensatedTiltDegrees)),
                CollectiveVertical = flight.CollectiveVertical,
                TiltThrust = flight.TiltThrust,
                MaxBank = math.radians(flight.BankAngleDegrees),
                BankResponse = flight.BankResponse,
                MaxTurnRate = math.radians(new float3(flight.MaxPitchRate, flight.MaxYawRate, flight.MaxRollRate)) * stats.TurnRateMultiplier,
                TurnResponse = flight.TurnResponse,
                QuadraticDrag = flight.QuadraticDrag,
                LinearDrag = flight.LinearDrag,
                Weathervane = flight.Weathervane,
                AutoLevel = flight.AutoLevel,
                DashBoost = flight.DashBoost,
                DashCooldown = flight.DashCooldown * stats.DashCooldownMultiplier,
            };
            built.Health = stats.Health;
            built.Crash = new CrashDamageConfig { SafeSpeedLoss = crash.SafeSpeedLoss, DamagePerSpeedLoss = crash.DamagePerSpeedLoss };

            BuildWeapons(chassis, modulesBySlot, sockets, resolveMissilePrefab, built);
            return built;
        }

        private static LoadoutStats Stats(ChassisDefinition chassis, IReadOnlyList<ModuleDefinition> modules, BuiltLoadout built)
        {
            if (chassis == null)
            {
                built.Validation = new LoadoutResult(LoadoutError.UnknownChassis);
                return new LoadoutStats(FallbackHealth, 0f, 0f, 1f, 1f, 1f);
            }

            var ids = new ushort[chassis.Slots.Count];
            for (var i = 0; i < ids.Length && i < modules.Count; i++)
                ids[i] = modules[i] != null ? modules[i].Id : LoadoutSelection.EmptySlot;

            var catalog = new ListCatalog(chassis, modules);
            var selection = new LoadoutSelection(chassis.Id, ids);
            built.Code = LoadoutCode.From(selection);
            built.Validation = LoadoutRules.Validate(selection, catalog, catalog);
            catalog.TryGetChassis(chassis.Id, out var spec);
            return LoadoutRules.Compute(spec, selection, catalog);
        }

        /// <summary>
        /// The nose module's hitscan weapon is the gun. Every pylon module with a rocket or missile becomes a pylon
        /// weapon (cycled in play). The first utility ability with flares gives the countermeasure.
        /// </summary>
        private static void BuildWeapons(ChassisDefinition chassis, IReadOnlyList<ModuleDefinition> modules,
            IReadOnlyList<ChassisSocket> sockets, Func<GameObject, Entity> resolveMissilePrefab, BuiltLoadout built)
        {
            var pylonIndex = 0;
            var gunFound = false;
            var flaresFound = false;
            for (var slot = 0; slot < modules.Count; slot++)
            {
                var module = modules[slot];
                if (module == null) continue;
                var slotType = chassis != null && slot < chassis.Slots.Count ? chassis.Slots[slot] : module.Slot;

                if (!gunFound && module.Weapon is HitscanWeaponDefinition gun)
                {
                    gunFound = true;
                    built.Gun = new HitscanWeaponConfig
                    {
                        FireInterval = gun.FireInterval,
                        Damage = gun.DamagePerShot,
                        Range = gun.Range,
                        MuzzleOffset = Muzzle(sockets, SlotType.Nose, 0, gun.MuzzleOffset, mirror: false),
                    };
                }

                if (!flaresFound && module.Ability is CountermeasureDefinition flares)
                {
                    flaresFound = true;
                    built.Countermeasure = new CountermeasureConfig { Cooldown = flares.Cooldown, DecoyDuration = flares.DecoyDuration };
                }

                if (slotType != SlotType.Pylon) continue;
                // Without a socket, odd pylons mirror the definition's launch point to the other side.
                var mirror = pylonIndex % 2 == 1;
                if (module.Weapon is RocketWeaponDefinition rocket)
                    built.Pylons.Add(RocketPylon(module, rocket, Muzzle(sockets, SlotType.Pylon, pylonIndex, rocket.MuzzleOffset, mirror)));
                else if (module.Weapon is GuidedMissileDefinition missile)
                    built.Pylons.Add(MissilePylon(module, missile, Muzzle(sockets, SlotType.Pylon, pylonIndex, missile.MuzzleOffset, mirror),
                        resolveMissilePrefab));
                pylonIndex++;
            }
        }

        /// <summary>Launch point: the matching socket if the chassis has one, else the definition's offset.</summary>
        private static float3 Muzzle(IReadOnlyList<ChassisSocket> sockets, SlotType slot, int index, Vector3 fallback, bool mirror)
        {
            if (sockets != null)
                foreach (var socket in sockets)
                    if (socket.Slot == slot && socket.Index == index) return socket.LocalPosition;

            float3 offset = fallback;
            if (mirror) offset.x = -offset.x;
            return offset;
        }

        private static PylonWeapon RocketPylon(ModuleDefinition module, RocketWeaponDefinition rocket, float3 muzzle) => new()
        {
            Behaviour = PylonBehaviour.Rocket,
            Name = ShortName(module),
            FireInterval = rocket.FireInterval,
            Speed = rocket.Speed,
            Lifetime = rocket.Lifetime,
            DirectDamage = rocket.DirectDamage,
            SplashDamage = rocket.SplashDamage,
            SplashRadius = rocket.SplashRadius,
            MuzzleOffset = muzzle,
        };

        private static PylonWeapon MissilePylon(ModuleDefinition module, GuidedMissileDefinition missile, float3 muzzle,
            Func<GameObject, Entity> resolveMissilePrefab) => new()
        {
            Behaviour = PylonBehaviour.Guided,
            Name = ShortName(module),
            FireInterval = missile.FireInterval,
            Speed = missile.Speed,
            Lifetime = missile.Lifetime,
            DirectDamage = missile.DirectDamage,
            SplashDamage = missile.SplashDamage,
            SplashRadius = missile.SplashRadius,
            MuzzleOffset = muzzle,
            Seeker = missile.Seeker,
            Targets = missile.Targets,
            LockConeCos = missile.LockConeCos,
            LockRange = missile.LockRange,
            LockTime = missile.LockTime,
            RequiresLock = missile.RequiresLock,
            TurnRate = missile.TurnRateRadians,
            NavigationGain = missile.NavigationGain,
            ProximityFuse = missile.ProximityFuse,
            ArmTime = missile.ArmTime,
            Cluster = missile.Cluster,
            SalvoCount = missile.SalvoCount,
            SalvoInterval = missile.SalvoInterval,
            SalvoConeCos = missile.SalvoConeCos,
            FlareResistance = missile.FlareResistance,
            MissilePrefab = missile.MissilePrefab != null && resolveMissilePrefab != null ? resolveMissilePrefab(missile.MissilePrefab) : Entity.Null,
        };

        /// <summary>HUD name: the module name, upper case, cut to fit.</summary>
        private static FixedString32Bytes ShortName(ModuleDefinition module)
        {
            var name = string.IsNullOrEmpty(module.DisplayName) ? module.name : module.DisplayName;
            name = name.ToUpperInvariant();
            return name.Length > 28 ? name.Substring(0, 28) : name;
        }

        /// <summary>A catalog of just the given assets, for <see cref="LoadoutRules"/>. Everything counts as unlocked.</summary>
        private sealed class ListCatalog : ILoadoutCatalog, IPlayerUnlocks
        {
            private readonly ChassisSpec _chassis;
            private readonly Dictionary<ushort, ModuleSpec> _modules = new();

            public ListCatalog(ChassisDefinition chassis, IEnumerable<ModuleDefinition> modules)
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
            public int Level => int.MaxValue;
            public bool Owns(ushort moduleId) => true;
        }
    }
}
