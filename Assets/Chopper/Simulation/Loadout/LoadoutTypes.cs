using System;
using System.Collections.Generic;

namespace Chopper.Simulation.Loadout
{
    /// <summary>Where a module fits on a chassis.</summary>
    public enum SlotType : byte
    {
        Nose = 0,
        Pylon = 1,
        Turret = 2,
        Utility = 3,
        Winch = 4,
        Rotor = 5,
        Engine = 6,
        Armour = 7,
    }

    /// <summary>What a module changes. Multipliers: 1 = no change.</summary>
    [Serializable]
    public struct ModuleStats
    {
        public float Weight;
        public float HealthBonus;
        public float LiftMultiplier;
        public float TurnRateMultiplier;
        public float DashCooldownMultiplier;

        public static ModuleStats None => new()
        {
            LiftMultiplier = 1f,
            TurnRateMultiplier = 1f,
            DashCooldownMultiplier = 1f,
        };
    }

    /// <summary>Plain data for one chassis. Made from a <see cref="ChassisDefinition"/> asset, or directly in tests.</summary>
    public sealed class ChassisSpec
    {
        public ushort Id { get; }
        public string Name { get; }
        public float BaseHealth { get; }
        /// <summary>Most module weight the chassis can carry.</summary>
        public float WeightBudget { get; }
        public IReadOnlyList<SlotType> Slots { get; }
        public int UnlockLevel { get; }

        public ChassisSpec(ushort id, string name, float baseHealth, float weightBudget, IReadOnlyList<SlotType> slots, int unlockLevel = 0)
        {
            Id = id;
            Name = name;
            BaseHealth = baseHealth;
            WeightBudget = weightBudget;
            Slots = slots;
            UnlockLevel = unlockLevel;
        }
    }

    /// <summary>Plain data for one module. Made from a <see cref="ModuleDefinition"/> asset, or directly in tests.</summary>
    public sealed class ModuleSpec
    {
        public ushort Id { get; }
        public string Name { get; }
        public SlotType Slot { get; }
        public ModuleStats Stats { get; }
        /// <summary>Player level needed to use it.</summary>
        public int UnlockLevel { get; }
        /// <summary>Empty = fits every chassis.</summary>
        public IReadOnlyCollection<ushort> AllowedChassis { get; }

        public ModuleSpec(ushort id, string name, SlotType slot, ModuleStats stats, int unlockLevel = 0,
            IReadOnlyCollection<ushort> allowedChassis = null)
        {
            Id = id;
            Name = name;
            Slot = slot;
            Stats = stats;
            UnlockLevel = unlockLevel;
            AllowedChassis = allowedChassis ?? Array.Empty<ushort>();
        }

        public bool FitsChassis(ushort chassisId)
        {
            if (AllowedChassis.Count == 0) return true;
            foreach (var id in AllowedChassis)
                if (id == chassisId) return true;
            return false;
        }
    }

    /// <summary>
    /// A player's choice: one chassis and one module id per chassis slot (same order as <see cref="ChassisSpec.Slots"/>).
    /// <see cref="EmptySlot"/> leaves a slot empty. Small ids, so it fits in a matchmaker ticket.
    /// </summary>
    public readonly struct LoadoutSelection
    {
        public const ushort EmptySlot = 0;

        public ushort ChassisId { get; }
        public IReadOnlyList<ushort> Modules { get; }

        public LoadoutSelection(ushort chassisId, IReadOnlyList<ushort> modules)
        {
            ChassisId = chassisId;
            Modules = modules ?? Array.Empty<ushort>();
        }
    }

    /// <summary>Looks up chassis and modules by id.</summary>
    public interface ILoadoutCatalog
    {
        bool TryGetChassis(ushort id, out ChassisSpec chassis);
        bool TryGetModule(ushort id, out ModuleSpec module);
    }

    /// <summary>What a player has unlocked. On the server this comes from Cloud Save, never from the client.</summary>
    public interface IPlayerUnlocks
    {
        int Level { get; }
        bool Owns(ushort moduleId);
    }

    public enum LoadoutError : byte
    {
        None = 0,
        UnknownChassis,
        ChassisLocked,
        WrongSlotCount,
        UnknownModule,
        WrongSlotType,
        WrongChassis,
        ModuleLocked,
        NotOwned,
        OverWeight,
    }

    public readonly struct LoadoutResult
    {
        public LoadoutError Error { get; }
        /// <summary>Slot that failed, or -1.</summary>
        public int Slot { get; }

        public bool IsValid => Error == LoadoutError.None;

        public LoadoutResult(LoadoutError error, int slot = -1)
        {
            Error = error;
            Slot = slot;
        }

        public override string ToString() => IsValid ? "Valid" : Slot >= 0 ? $"{Error} (slot {Slot})" : Error.ToString();
    }
}
