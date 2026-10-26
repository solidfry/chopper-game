using System;

namespace Chopper.Simulation.Loadout
{
    /// <summary>Final numbers for a chassis with its modules. Used by the hangar (stat bars) and the server (spawn).</summary>
    public readonly struct LoadoutStats
    {
        public float Health { get; }
        public float Weight { get; }
        public float WeightBudget { get; }
        public float LiftMultiplier { get; }
        public float TurnRateMultiplier { get; }
        public float DashCooldownMultiplier { get; }

        /// <summary>0 = empty, 1 = full budget.</summary>
        public float WeightFraction => WeightBudget > 0f ? Weight / WeightBudget : 0f;

        public LoadoutStats(float health, float weight, float weightBudget, float lift, float turnRate, float dashCooldown)
        {
            Health = health;
            Weight = weight;
            WeightBudget = weightBudget;
            LiftMultiplier = lift;
            TurnRateMultiplier = turnRate;
            DashCooldownMultiplier = dashCooldown;
        }
    }

    /// <summary>
    /// Loadout rules, shared by the hangar and the server. The server always validates: a client can send any ids.
    /// </summary>
    public static class LoadoutRules
    {
        /// <summary>At full weight budget a chopper turns this much slower...</summary>
        public const float FullWeightTurnPenalty = 0.15f;
        /// <summary>...and has this much less lift.</summary>
        public const float FullWeightLiftPenalty = 0.1f;

        public static LoadoutResult Validate(in LoadoutSelection selection, ILoadoutCatalog catalog, IPlayerUnlocks unlocks)
        {
            if (!catalog.TryGetChassis(selection.ChassisId, out var chassis))
                return new LoadoutResult(LoadoutError.UnknownChassis);
            if (unlocks.Level < chassis.UnlockLevel)
                return new LoadoutResult(LoadoutError.ChassisLocked);
            if (selection.Modules.Count != chassis.Slots.Count)
                return new LoadoutResult(LoadoutError.WrongSlotCount);

            var weight = 0f;
            for (var slot = 0; slot < chassis.Slots.Count; slot++)
            {
                var moduleId = selection.Modules[slot];
                if (moduleId == LoadoutSelection.EmptySlot) continue;

                if (!catalog.TryGetModule(moduleId, out var module))
                    return new LoadoutResult(LoadoutError.UnknownModule, slot);
                if (module.Slot != chassis.Slots[slot])
                    return new LoadoutResult(LoadoutError.WrongSlotType, slot);
                if (!module.FitsChassis(chassis.Id))
                    return new LoadoutResult(LoadoutError.WrongChassis, slot);
                if (unlocks.Level < module.UnlockLevel)
                    return new LoadoutResult(LoadoutError.ModuleLocked, slot);
                if (!unlocks.Owns(moduleId))
                    return new LoadoutResult(LoadoutError.NotOwned, slot);

                weight += module.Stats.Weight;
            }

            return weight > chassis.WeightBudget
                ? new LoadoutResult(LoadoutError.OverWeight)
                : new LoadoutResult(LoadoutError.None);
        }

        /// <summary>Stats for a selection. Unknown modules are skipped; call <see cref="Validate"/> first on the server.</summary>
        public static LoadoutStats Compute(ChassisSpec chassis, in LoadoutSelection selection, ILoadoutCatalog catalog)
        {
            var health = chassis.BaseHealth;
            var weight = 0f;
            var lift = 1f;
            var turnRate = 1f;
            var dashCooldown = 1f;

            foreach (var moduleId in selection.Modules)
            {
                if (moduleId == LoadoutSelection.EmptySlot || !catalog.TryGetModule(moduleId, out var module)) continue;
                var stats = module.Stats;
                health += stats.HealthBonus;
                weight += stats.Weight;
                lift *= stats.LiftMultiplier;
                turnRate *= stats.TurnRateMultiplier;
                dashCooldown *= stats.DashCooldownMultiplier;
            }

            // Weight makes every choice cost something: heavier = slower to turn and climb.
            var weightFraction = chassis.WeightBudget > 0f ? Math.Clamp(weight / chassis.WeightBudget, 0f, 1f) : 0f;
            turnRate *= 1f - FullWeightTurnPenalty * weightFraction;
            lift *= 1f - FullWeightLiftPenalty * weightFraction;

            return new LoadoutStats(health, weight, chassis.WeightBudget, lift, turnRate, dashCooldown);
        }
    }
}
