using Unity.Mathematics;

namespace Chopper.Simulation.Combat
{
    /// <summary>
    /// Heat of a nose gun. Each shot adds heat; it cools when the gun stops. At full heat the gun locks out until it has
    /// cooled to <see cref="ResumeHeat"/>. Shared by the fire system and tests; client and server run the same maths.
    /// </summary>
    public static class GunHeat
    {
        /// <summary>An overheated gun fires again at this heat (0..1).</summary>
        public const float ResumeHeat = 0.4f;
        /// <summary>Seconds after the next shot was due before the gun starts to cool. Steady fire never cools.</summary>
        public const float CoolPause = 0.2f;

        /// <summary>Call once per tick, before <see cref="CanFire"/>.</summary>
        public static void Tick(ref HitscanWeaponState state, in HitscanWeaponConfig config, float deltaTime)
        {
            if (config.HeatPerShot <= 0f) return;
            state.CoolDelay = math.max(0f, state.CoolDelay - deltaTime);
            if (!state.Overheated && state.CoolDelay > 0f) return;

            state.Heat = math.max(0f, state.Heat - config.CoolRate * deltaTime);
            if (state.Overheated && state.Heat <= ResumeHeat) state.Overheated = false;
        }

        public static bool CanFire(in HitscanWeaponState state) => !state.Overheated;

        /// <summary>Call for each shot. Does nothing for a gun with no heat limit (<see cref="HitscanWeaponConfig.HeatPerShot"/> 0).</summary>
        public static void AddShot(ref HitscanWeaponState state, in HitscanWeaponConfig config)
        {
            if (config.HeatPerShot <= 0f) return;
            state.Heat = math.min(1f, state.Heat + config.HeatPerShot);
            state.CoolDelay = config.FireInterval + CoolPause;
            if (state.Heat >= 1f) state.Overheated = true;
        }
    }

    /// <summary>
    /// A magazine that stores rounds used, so the default state (0) is a full magazine: a new or respawned chopper needs
    /// no setup. It refills fully when no shot was fired for the reload time. Size 0 = no limit. Used by pylon weapons and
    /// flares; a salvo counts as one round.
    /// </summary>
    public static class Magazine
    {
        /// <summary>Call once per tick, before <see cref="HasRound"/>.</summary>
        public static void Tick(ref byte roundsUsed, ref float reloadRemaining, float deltaTime)
        {
            if (roundsUsed == 0) return;
            reloadRemaining -= deltaTime;
            if (reloadRemaining > 0f) return;
            roundsUsed = 0;
            reloadRemaining = 0f;
        }

        public static bool HasRound(byte roundsUsed, byte size) => size == 0 || roundsUsed < size;

        /// <summary>Call for each shot. Each shot starts the reload time again.</summary>
        public static void Spend(ref byte roundsUsed, ref float reloadRemaining, byte size, float reloadSeconds)
        {
            if (size == 0) return;
            if (roundsUsed < size) roundsUsed++;
            reloadRemaining = reloadSeconds;
        }

        public static int RoundsLeft(byte roundsUsed, byte size) => math.max(0, size - roundsUsed);

        /// <summary>How far the refill has come: 0 = just fired, 1 = full (also for a full magazine or no limit).</summary>
        public static float ReloadProgress(byte roundsUsed, float reloadRemaining, float reloadSeconds) =>
            roundsUsed == 0 || reloadSeconds <= 0f ? 1f : math.saturate(1f - reloadRemaining / reloadSeconds);
    }
}
