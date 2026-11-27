using System;
using System.Collections.Generic;
using Chopper.Simulation.Definitions;

namespace Chopper.Presentation.Hangar
{
    /// <summary>
    /// Hangar weapon numbers (Docs/WeaponFeelPlan.md step 5): rows for the shown weapon, each bar against the best in the
    /// slot, with the change from the fitted one (the same now / preview pair as the chopper stat rows). Pure.
    /// </summary>
    public static class WeaponStatRows
    {
        /// <summary>Seconds of fire shown for a gun with no heat limit (bar full).</summary>
        public const float NoHeatLimitSeconds = 60f;

        private readonly struct Metric
        {
            public readonly string Label;
            public readonly string Unit;
            /// <summary>Value, or NaN when the weapon has no such number.</summary>
            public readonly Func<WeaponDefinition, float> Get;
            public readonly bool HigherIsBetter;
            public readonly Func<float, string> Format;

            public Metric(string label, string unit, Func<WeaponDefinition, float> get, bool higherIsBetter, Func<float, string> format)
            {
                Label = label;
                Unit = unit;
                Get = get;
                HigherIsBetter = higherIsBetter;
                Format = format;
            }
        }

        private static string Whole(float v) => $"{v:0}";
        private static string Tenths(float v) => $"{v:0.0}";

        private static readonly Metric[] GunMetrics =
        {
            new("DAMAGE", "HP", w => w is HitscanWeaponDefinition g ? g.DamagePerShot : float.NaN, true, Whole),
            new("DPS", "HP/S", w => w is HitscanWeaponDefinition g ? g.DamagePerSecond : float.NaN, true, Whole),
            new("RATE", "RPM", w => w is HitscanWeaponDefinition g ? 60f / g.FireInterval : float.NaN, true, Whole),
            new("RANGE", "M", w => w is HitscanWeaponDefinition g ? g.Range : float.NaN, true, Whole),
            new("HEAT", "S", w => w is HitscanWeaponDefinition g ? HeatSeconds(g) : float.NaN, true,
                v => v >= NoHeatLimitSeconds ? "NO LIMIT" : Tenths(v)),
        };

        private static readonly Metric[] RocketMetrics =
        {
            new("DAMAGE", "HP", w => w is HitscanWeaponDefinition ? float.NaN : Damage(w), true, Whole),
            new("BLAST", "M", w => w switch
            {
                RocketWeaponDefinition r => r.SplashRadius,
                GuidedMissileDefinition m => m.SplashRadius,
                _ => float.NaN,
            }, true, Tenths),
            new("LOCK", "S", w => w is GuidedMissileDefinition m && m.RequiresLock ? m.LockTime : float.NaN, false, Tenths),
            new("RATE", "S", w => w switch
            {
                RocketWeaponDefinition r => r.FireInterval,
                GuidedMissileDefinition m => m.FireInterval,
                _ => float.NaN,
            }, false, Tenths),
            new("MAGAZINE", "", w => w switch
            {
                RocketWeaponDefinition r => r.MagazineSize,
                GuidedMissileDefinition m => m.MagazineSize,
                _ => float.NaN,
            }, true, v => v <= 0f ? "NO LIMIT" : Whole(v)),
            new("RELOAD", "S", w => w switch
            {
                RocketWeaponDefinition r when r.MagazineSize > 0 => r.ReloadSeconds,
                GuidedMissileDefinition m when m.MagazineSize > 0 => m.ReloadSeconds,
                _ => float.NaN,
            }, false, Tenths),
            new("SPEED", "M/S", w => w switch
            {
                RocketWeaponDefinition r => r.Speed,
                GuidedMissileDefinition m => m.Speed,
                _ => float.NaN,
            }, true, Whole),
        };

        /// <summary>Seconds of steady fire before the gun overheats; <see cref="NoHeatLimitSeconds"/> when it never does.</summary>
        public static float HeatSeconds(HitscanWeaponDefinition gun) =>
            gun.HeatPerShot > 0f ? Math.Min(gun.FireInterval / gun.HeatPerShot, NoHeatLimitSeconds) : NoHeatLimitSeconds;

        /// <summary>Damage of one round (direct + splash), per salvo round.</summary>
        public static float Damage(WeaponDefinition weapon) => weapon switch
        {
            RocketWeaponDefinition r => r.DirectDamage + r.SplashDamage,
            GuidedMissileDefinition m => m.DirectDamage + m.SplashDamage,
            HitscanWeaponDefinition g => g.DamagePerShot,
            _ => float.NaN,
        };

        /// <summary>
        /// Rows for <paramref name="shown"/> (hovered, or the fitted one). <paramref name="fitted"/> may be null or of
        /// another kind (then no change shows). <paramref name="slotWeapons"/>: every weapon that fits the slot.
        /// Empty when <paramref name="shown"/> is not a gun, rocket or missile.
        /// </summary>
        public static void Build(WeaponDefinition shown, WeaponDefinition fitted, IEnumerable<WeaponDefinition> slotWeapons,
            List<HangarStatRow> rows)
        {
            rows.Clear();
            var metrics = shown switch
            {
                HitscanWeaponDefinition => GunMetrics,
                RocketWeaponDefinition or GuidedMissileDefinition => RocketMetrics,
                _ => null,
            };
            if (metrics == null) return;

            foreach (var metric in metrics)
            {
                var value = metric.Get(shown);
                if (float.IsNaN(value)) continue;
                var best = Best(metric, slotWeapons, value);
                var preview = Fraction(metric, value, best);
                var before = fitted != null ? metric.Get(fitted) : float.NaN;
                var now = float.IsNaN(before) ? preview : Fraction(metric, before, best);
                rows.Add(new HangarStatRow(metric.Label, now, preview, metric.Format(value), metric.Unit, headline: false));
            }
        }

        private static float Best(in Metric metric, IEnumerable<WeaponDefinition> weapons, float seed)
        {
            var best = seed;
            if (weapons == null) return best;
            foreach (var weapon in weapons)
            {
                if (weapon == null) continue;
                var value = metric.Get(weapon);
                if (float.IsNaN(value)) continue;
                best = metric.HigherIsBetter ? Math.Max(best, Score(metric, value)) : Math.Min(best, value);
            }
            return metric.HigherIsBetter ? Math.Max(best, Score(metric, seed)) : best;
        }

        /// <summary>Magazine 0 means no limit: it scores as the best.</summary>
        private static float Score(in Metric metric, float value) =>
            metric.Label == "MAGAZINE" && value <= 0f ? 255f : value;

        private static float Fraction(in Metric metric, float value, float best)
        {
            if (metric.HigherIsBetter)
                return best > 0f ? Math.Clamp(Score(metric, value) / best, 0f, 1f) : 0f;
            return value > 0f ? Math.Clamp(best / value, 0f, 1f) : 1f;
        }
    }
}
