using System;
using System.Collections.Generic;
using Chopper.Simulation.Config;
using Chopper.Simulation.Visuals;
using UnityEngine;

namespace Chopper.Presentation.Visuals
{
    /// <summary>
    /// The look of each <see cref="TimeOfDay"/> and <see cref="Weather"/> (cosmetic). Hangs off the GameConfig.
    /// Now (5 Nov 2026): Setup step 10 bakes one look (the map's default, Night + Clear) into each city match scene:
    /// sun, ambient light, background, and reflection probes baked under that light.
    /// Planned, not built: the server picks the conditions per match; a client presentation system applies the look
    /// at load (sun, ambient, fog tint and density, city lights on or off, wet ground, weather particles), and the
    /// probes get one baked set per time of day (<c>Art/City/&lt;kit&gt;/Probes/&lt;time&gt;</c>), swapped at load.
    /// Add a look here for each new time or weather; a missing one falls back to the first look in the list.
    /// </summary>
    [CreateAssetMenu(menuName = "Chopper/Environment Settings", fileName = "EnvironmentSettings")]
    public sealed class EnvironmentSettings : ScriptableObject, IValidatedAsset
    {
        [Serializable]
        public sealed class TimeOfDayLook
        {
            public TimeOfDay Time = TimeOfDay.Night;
            [Tooltip("Sun (or moon) light colour.")] public Color SunColour = new(0.55f, 0.62f, 0.85f);
            [Min(0f)] public float SunIntensity = 0.25f;
            [Tooltip("Sun height above the horizon (degrees) and compass direction it shines towards (degrees).")]
            public Vector2 SunPitchYaw = new(35f, 140f);
            [Tooltip("Ambient light (trilight): sky, horizon, ground. Linear colours.")]
            public Color AmbientSky = new(0.06f, 0.065f, 0.09f);
            public Color AmbientEquator = new(0.05f, 0.045f, 0.045f);
            public Color AmbientGround = new(0.03f, 0.025f, 0.02f);
            [Tooltip("Camera background behind the backdrop (no sky box yet).")] public Color Background = new(0.03f, 0.03f, 0.045f);
            [Tooltip("Planned: x the map's height fog colour.")] public Color FogTint = Color.white;
            [Tooltip("Planned: street lamps, floodlights and window glow on.")] public bool CityLights = true;
        }

        [Serializable]
        public sealed class WeatherLook
        {
            public Weather Weather = Weather.Clear;
            [Tooltip("Planned: x the map's height fog density.")] [Min(0f)] public float FogDensityScale = 1f;
            [Tooltip("Planned: fog colour this weather pulls towards; alpha = how much.")] public Color FogColour = new(0f, 0f, 0f, 0f);
            [Tooltip("Planned: wet ground and puddles (0 = dry).")] [Range(0f, 1f)] public float Wetness;
            [Tooltip("Planned: particles that follow the camera (rain, sand, snow, ash). Empty = none.")] public GameObject Effect;
        }

        [SerializeField] private List<TimeOfDayLook> times = new();
        [SerializeField] private List<WeatherLook> weathers = new();

        public IReadOnlyList<TimeOfDayLook> Times => times;
        public IReadOnlyList<WeatherLook> Weathers => weathers;

        public TimeOfDayLook Look(TimeOfDay time)
        {
            foreach (var look in times) if (look != null && look.Time == time) return look;
            return times.Count > 0 ? times[0] : null;
        }

        public WeatherLook Look(Weather weather)
        {
            foreach (var look in weathers) if (look != null && look.Weather == weather) return look;
            return weathers.Count > 0 ? weathers[0] : null;
        }

        public bool Has(TimeOfDay time) => times.Exists(l => l != null && l.Time == time);
        public bool Has(Weather weather) => weathers.Exists(l => l != null && l.Weather == weather);

        /// <summary>Editor only: the setup tool fills a new asset with the looks that exist now.</summary>
        public void Initialise(TimeOfDayLook time, WeatherLook weather)
        {
            times = new List<TimeOfDayLook> { time };
            weathers = new List<WeatherLook> { weather };
        }

        public void CollectProblems(List<string> problems)
        {
            if (times.Count == 0) problems.Add($"{name}: no time of day look.");
            if (weathers.Count == 0) problems.Add($"{name}: no weather look.");
        }
    }
}
