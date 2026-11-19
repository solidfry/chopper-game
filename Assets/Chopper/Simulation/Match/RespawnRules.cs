using Unity.Mathematics;
using Unity.NetCode;

namespace Chopper.Simulation.Match
{
    /// <summary>
    /// How long a dead player waits before the next chopper, for one mode. Part of <see cref="MatchRules"/>.
    /// The delay is <see cref="Base"/> + <see cref="PerDeath"/> × deaths so far, at most <see cref="Cap"/>.
    /// With <see cref="Wave"/> &gt; 0, players come back together on the next wave (every <see cref="Wave"/> s of match
    /// time), but never sooner than that delay.
    /// </summary>
    [System.Serializable]
    public struct RespawnRule
    {
        /// <summary>Seconds for the first death.</summary>
        public float Base;
        /// <summary>Seconds added for each earlier death in this match.</summary>
        public float PerDeath;
        /// <summary>Longest delay in seconds (before the wave wait). 0 = no cap.</summary>
        public float Cap;
        /// <summary>Wave length in seconds of match time. 0 = no waves.</summary>
        public float Wave;

        /// <summary>Seconds until the chopper spawns. <paramref name="deaths"/> = earlier deaths in this match;
        /// <paramref name="matchElapsed"/> = seconds since the match started.</summary>
        public float Delay(int deaths, float matchElapsed)
        {
            var delay = math.max(0f, Base + PerDeath * math.max(0, deaths));
            if (Cap > 0f) delay = math.min(delay, Cap);
            if (Wave <= 0f) return delay;

            // The first wave at or after the earliest spawn time.
            var earliest = math.max(0f, matchElapsed) + delay;
            var wave = math.ceil(earliest / Wave - 1e-4f) * Wave;
            return math.max(delay, wave - math.max(0f, matchElapsed));
        }

        public static RespawnRule FreeForAll => new() { Base = 3f, PerDeath = 0f, Cap = 3f, Wave = 0f };
        public static RespawnRule Team => new() { Base = 5f, PerDeath = 0.5f, Cap = 10f, Wave = 0f };
    }

    /// <summary>Server → the dead player: seconds until the next chopper. The HUD counts it down.</summary>
    public struct RespawnTimeRpc : IRpcCommand
    {
        public float Seconds;
    }
}
