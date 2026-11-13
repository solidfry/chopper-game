using System;
using Chopper.Simulation.Visuals;
using Unity.Entities;
using Unity.NetCode;

namespace Chopper.Simulation.Session
{
    /// <summary>
    /// Server: once the map's subscene is loaded (<see cref="MatchMap"/>), picks the match's time of day and weather
    /// at random from the map's allowed sets and keeps them as the <see cref="MatchConditions"/> singleton. The map
    /// announcement sends them to every client. Command-line overrides for tests: <c>-time Day</c>,
    /// <c>-weather Rain</c> (names of <see cref="TimeOfDay"/> and <see cref="Weather"/>, any case).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial class MatchConditionsPickSystem : SystemBase
    {
        private TimeOfDay? _timeOverride;
        private Weather? _weatherOverride;
        private Unity.Mathematics.Random _random;

        protected override void OnCreate()
        {
            RequireForUpdate<MatchMap>();
            _random = new Unity.Mathematics.Random((uint)(DateTime.UtcNow.Ticks & 0x7fffffff) | 1u);
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "-time", StringComparison.OrdinalIgnoreCase) &&
                    Enum.TryParse<TimeOfDay>(args[i + 1], true, out var time))
                    _timeOverride = time;
                if (string.Equals(args[i], "-weather", StringComparison.OrdinalIgnoreCase) &&
                    Enum.TryParse<Weather>(args[i + 1], true, out var weather))
                    _weatherOverride = weather;
            }
        }

        protected override void OnUpdate()
        {
            if (SystemAPI.HasSingleton<MatchConditions>()) return;
            var map = SystemAPI.GetSingleton<MatchMap>();
            var conditions = new MatchConditions
            {
                Time = _timeOverride ?? Conditions.PickTime(map.AllowedTimes, _random.NextFloat()),
                Weather = _weatherOverride ?? Conditions.PickWeather(map.AllowedWeathers, _random.NextFloat()),
            };
            EntityManager.AddComponentData(EntityManager.CreateEntity(), conditions);
            UnityEngine.Debug.Log($"Match conditions: {conditions.Time}, {conditions.Weather} (map {map.MapId}).");
        }
    }
}
