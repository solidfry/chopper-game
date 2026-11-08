using System;
using Chopper.Simulation.Combat;
using UnityEngine;

namespace Chopper.Simulation.Bots
{
    /// <summary>When a bot uses an ability or gadget. Each kind reads <see cref="BotUse.Threshold"/> its own way.</summary>
    public enum BotTrigger : byte
    {
        /// <summary>The default rule for this ability kind (<see cref="BotUseRules"/>).</summary>
        Auto = 0,
        Never,
        /// <summary>A missile is coming at the bot: time to impact (s) below the threshold.</summary>
        IncomingMissile,
        /// <summary>An enemy seeker is locking or has locked the bot.</summary>
        BeingLocked,
        /// <summary>Health share below the threshold (0..1).</summary>
        LowHealth,
        /// <summary>The bot took damage in the last threshold seconds.</summary>
        UnderFire,
        /// <summary>The target is inside the threshold distance (m), in front, in sight: just before shooting.</summary>
        AttackRun,
        /// <summary>The target is farther than the threshold (m) and the bot is going after it.</summary>
        Chase,
        /// <summary>The bot is evading or retreating.</summary>
        Escape,
        /// <summary>An enemy is within the threshold distance (m).</summary>
        EnemiesNear,
        /// <summary>The bot sees no enemy.</summary>
        NoTarget,
        /// <summary>The target is within the threshold (m) and closing: ram it.</summary>
        Ram,
    }

    /// <summary>
    /// A bot's rule for one ability or gadget. Lives on the definition (pilot ability, countermeasure module), so a new
    /// ability is usable by bots without a change to the brain.
    /// </summary>
    [Serializable]
    public struct BotUse
    {
        [Tooltip("When a bot uses it. Auto = the default for this kind.")]
        public BotTrigger Trigger;
        [Tooltip("Trigger value: seconds, metres or a health share (see the trigger). 0 with Auto = the default.")]
        public float Threshold;

        public BotUse(BotTrigger trigger, float threshold)
        {
            Trigger = trigger;
            Threshold = threshold;
        }
    }

    /// <summary>The defaults for <see cref="BotTrigger.Auto"/>.</summary>
    public static class BotUseRules
    {
        public static BotUse Resolve(in BotUse use, AbilityKind kind)
        {
            if (use.Trigger != BotTrigger.Auto) return use;
            var rule = ForAbility(kind);
            if (use.Threshold > 0f) rule.Threshold = use.Threshold;
            return rule;
        }

        public static BotUse ForAbility(AbilityKind kind) => kind switch
        {
            AbilityKind.NaniteCloud => new BotUse(BotTrigger.LowHealth, 0.5f),
            AbilityKind.VenomBelt => new BotUse(BotTrigger.AttackRun, 350f),
            AbilityKind.NapalmSalvo => new BotUse(BotTrigger.AttackRun, 300f),
            AbilityKind.Ghost => new BotUse(BotTrigger.BeingLocked, 0f),
            AbilityKind.Aegis => new BotUse(BotTrigger.UnderFire, 1.5f),
            AbilityKind.Overclock => new BotUse(BotTrigger.Chase, 400f),
            AbilityKind.Ping => new BotUse(BotTrigger.NoTarget, 0f),
            AbilityKind.Blackout => new BotUse(BotTrigger.BeingLocked, 0f),
            AbilityKind.StripDown => new BotUse(BotTrigger.IncomingMissile, 2f),
            AbilityKind.BatteringRam => new BotUse(BotTrigger.Ram, 90f),
            _ => new BotUse(BotTrigger.Never, 0f),
        };

        /// <summary>Flares: shortly before a missile hits.</summary>
        public static BotUse Flares(in BotUse use) =>
            use.Trigger != BotTrigger.Auto ? use : new BotUse(BotTrigger.IncomingMissile, use.Threshold > 0f ? use.Threshold : 1.1f);
    }
}
