namespace Chopper.Simulation.Visuals
{
    /// <summary>
    /// Time of day of a match. The server picks it per match from the map's allowed set (<see cref="MatchConditions"/>)
    /// and sends it with the map announcement, so every client sees the same light; the client's environment driver
    /// (<c>Presentation EnvironmentSystem</c>) turns it into sun, sky, fog and city lights.
    /// </summary>
    public enum TimeOfDay : byte
    {
        Morning,
        Day,
        Afternoon,
        Night,
    }

    /// <summary>
    /// Weather of a match, picked like <see cref="TimeOfDay"/> (server picks, clients show). Weather may later
    /// change play (sight distance, wind), so it lives in Simulation.
    /// </summary>
    public enum Weather : byte
    {
        Clear,
        Rain,
        Sandstorm,
        Fog,
        Snow,
        Ash,
    }

    /// <summary>
    /// The match's time of day and weather. Server: picked once the map's subscene is loaded
    /// (<see cref="Session.MatchConditionsPickSystem"/>). Client: from the map announcement. One singleton per world.
    /// </summary>
    public struct MatchConditions : Unity.Entities.IComponentData
    {
        public TimeOfDay Time;
        public Weather Weather;
    }

    /// <summary>Pure helpers for allowed sets (bit masks: bit n = enum value n) and the pick. Burst friendly.</summary>
    public static class Conditions
    {
        public const TimeOfDay DefaultTime = TimeOfDay.Night;
        public const Weather DefaultWeather = Weather.Clear;

        public static byte Mask(System.Collections.Generic.IEnumerable<TimeOfDay> values)
        {
            byte mask = 0;
            if (values != null) foreach (var value in values) mask |= (byte)(1 << (int)value);
            return mask;
        }

        public static byte Mask(System.Collections.Generic.IEnumerable<Weather> values)
        {
            byte mask = 0;
            if (values != null) foreach (var value in values) mask |= (byte)(1 << (int)value);
            return mask;
        }

        /// <summary>One of the set bits of <paramref name="mask"/>, chosen by <paramref name="roll"/> (0..1);
        /// <paramref name="fallback"/> when the mask is empty.</summary>
        public static int Pick(byte mask, float roll, int fallback)
        {
            var count = 0;
            for (var bit = 0; bit < 8; bit++) if ((mask & (1 << bit)) != 0) count++;
            if (count == 0) return fallback;
            var target = (int)(roll * count);
            if (target >= count) target = count - 1;
            if (target < 0) target = 0;
            for (var bit = 0; bit < 8; bit++)
            {
                if ((mask & (1 << bit)) == 0) continue;
                if (target-- == 0) return bit;
            }
            return fallback;
        }

        public static TimeOfDay PickTime(byte mask, float roll) => (TimeOfDay)Pick(mask, roll, (int)DefaultTime);
        public static Weather PickWeather(byte mask, float roll) => (Weather)Pick(mask, roll, (int)DefaultWeather);
    }
}
