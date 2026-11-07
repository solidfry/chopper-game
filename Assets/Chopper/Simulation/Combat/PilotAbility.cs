using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Chopper.Simulation.Combat
{
    /// <summary>The pilots' active abilities (plan section 5b, step 4). One per pilot; key F or gamepad D-pad up.</summary>
    public enum AbilityKind : byte
    {
        None = 0,
        /// <summary>APOTHECARY: heal <c>Strength</c> of the hull over the duration; hits do not stop it.</summary>
        NaniteCloud = 1,
        /// <summary>VIPER: corrosion from gun hits x <c>Strength</c>.</summary>
        VenomBelt = 2,
        /// <summary>PYRE: burn from blast hits x <c>Strength</c>.</summary>
        NapalmSalvo = 3,
        /// <summary>WRAITH: seekers cannot lock, missiles in flight lose you (the flare decoy window, no flares).</summary>
        Ghost = 4,
        /// <summary>BULWARK: a shield that takes the next <c>Strength</c> damage.</summary>
        Aegis = 5,
        /// <summary>HOTWIRE: the boost tank does not drain.</summary>
        Overclock = 6,
        /// <summary>OVERWATCH: every enemy shows on the HUD at any range, with health.</summary>
        Ping = 7,
        /// <summary>HEX: enemies within <c>Radius</c> lose their locks and cannot lock for <c>Strength</c> seconds.</summary>
        Blackout = 8,
        /// <summary>SCRAPPER: flares out (decoy) and endless boost.</summary>
        StripDown = 9,
        /// <summary>RAMJET: endless boost, and crashes and rams do not hurt you.</summary>
        BatteringRam = 10,
    }

    /// <summary>The chopper's ability, from its pilot (<see cref="Loadout.PilotAbilitySettings"/>). Same on client and server.</summary>
    public struct AbilityConfig : IComponentData
    {
        public AbilityKind Kind;
        public float Cooldown;
        /// <summary>Seconds it stays on.</summary>
        public float Duration;
        /// <summary>Kind-specific: heal share, damage-over-time scale, shield points, jam seconds.</summary>
        public float Strength;
        /// <summary>Blackout: reach (m).</summary>
        public float Radius;
    }

    /// <summary>
    /// Predicted on the owner, decided on the server, sent to everyone (the effects show on every chopper). Timers are
    /// unquantized: predicted code tests them against 0 (see the Handoff note on double rockets).
    /// </summary>
    public struct AbilityState : IComponentData
    {
        [GhostField] public float ActiveLeft;
        [GhostField] public float CooldownLeft;
        /// <summary>Hit by a Blackout: no locks until it runs out. Server writes it.</summary>
        [GhostField] public float JammedLeft;
        /// <summary>Goes up at each use: clients play the start effect and sound.</summary>
        [GhostField] public uint Uses;
        /// <summary>Server only (not sent): Aegis shield points left.</summary>
        public float Shield;

        public bool Active => ActiveLeft > 0f;
        public bool Jammed => JammedLeft > 0f;
    }

    /// <summary>The ability rules, pure (edit-mode tests: AbilityTests).</summary>
    public static class Abilities
    {
        /// <summary>Starts the ability when it has one and is not cooling down. True when it started.</summary>
        public static bool TryActivate(ref AbilityState state, in AbilityConfig config)
        {
            if (config.Kind == AbilityKind.None || state.CooldownLeft > 0f || state.Active) return false;
            state.ActiveLeft = math.max(config.Duration, 0.05f);
            state.CooldownLeft = math.max(config.Cooldown, state.ActiveLeft);
            state.Uses++;
            if (config.Kind == AbilityKind.Aegis) state.Shield = math.max(config.Strength, 0f);
            return true;
        }

        /// <summary>Counts the timers down. The shield ends with the ability.</summary>
        public static void Tick(ref AbilityState state, float deltaTime)
        {
            state.ActiveLeft = math.max(0f, state.ActiveLeft - deltaTime);
            state.CooldownLeft = math.max(0f, state.CooldownLeft - deltaTime);
            state.JammedLeft = math.max(0f, state.JammedLeft - deltaTime);
            if (!state.Active) state.Shield = 0f;
        }

        public static bool EndlessBoost(in AbilityConfig config, in AbilityState state) =>
            state.Active && config.Kind is AbilityKind.Overclock or AbilityKind.StripDown or AbilityKind.BatteringRam;

        public static bool CrashImmune(in AbilityConfig config, in AbilityState state) =>
            state.Active && config.Kind == AbilityKind.BatteringRam;

        public static bool ShowsAllEnemies(in AbilityConfig config, in AbilityState state) =>
            state.Active && config.Kind == AbilityKind.Ping;

        /// <summary>The ability's damage-over-time scale for one of this attacker's hits (1 = none).</summary>
        public static float DotScale(in AbilityConfig config, in AbilityState state, DamageKind hitKind)
        {
            if (!state.Active || config.Strength <= 0f) return 1f;
            if (config.Kind == AbilityKind.VenomBelt && hitKind == DamageKind.Bullet) return config.Strength;
            if (config.Kind == AbilityKind.NapalmSalvo && hitKind == DamageKind.Explosion) return config.Strength;
            return 1f;
        }

        /// <summary>Health per second while Nanite Cloud runs (0 otherwise).</summary>
        public static float HealPerSecond(in AbilityConfig config, in AbilityState state, float maxHealth) =>
            state.Active && config.Kind == AbilityKind.NaniteCloud && config.Duration > 0f
                ? maxHealth * config.Strength / config.Duration
                : 0f;

        /// <summary>The Aegis shield takes what it can. Returns the damage that gets through.</summary>
        public static float Absorb(ref AbilityState state, float amount)
        {
            if (state.Shield <= 0f || amount <= 0f) return amount;
            var taken = math.min(state.Shield, amount);
            state.Shield -= taken;
            return amount - taken;
        }
    }
}
