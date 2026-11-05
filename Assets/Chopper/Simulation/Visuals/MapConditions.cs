namespace Chopper.Simulation.Visuals
{
    /// <summary>
    /// Time of day of a match. Planned: the server picks it per match (from the map's allowed set) and replicates it,
    /// so every client sees the same light; the client's environment look (<c>Presentation EnvironmentSettings</c>)
    /// turns it into sun, sky, fog and city lights. Now (5 Nov 2026) only <see cref="Night"/> has a look, and the
    /// match scenes are built with it (Setup step 10).
    /// </summary>
    public enum TimeOfDay : byte
    {
        Morning,
        Day,
        Afternoon,
        Night,
    }

    /// <summary>
    /// Weather of a match. Planned like <see cref="TimeOfDay"/> (server picks, clients show). Weather may later
    /// change play (sight distance, wind), so it lives in Simulation. Now only <see cref="Clear"/> has a look.
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
}
