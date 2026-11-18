using System;
using System.Collections.Generic;
using Chopper.Simulation.Config;
using Chopper.Simulation.Visuals;
using UnityEngine;

namespace Chopper.Presentation.Visuals
{
    /// <summary>
    /// The look of each <see cref="TimeOfDay"/> and <see cref="Weather"/> (cosmetic). Hangs off the GameConfig.
    /// 13 Nov: the server now picks the conditions and the client <c>EnvironmentSystem</c> applies them through the
    /// scene's <c>EnvironmentDriver</c> (UniStorm) or, with none, this look. The notes below are the older plan.
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
            [Tooltip("x the map's height fog colour (city fog pass).")] public Color FogTint = Color.white;
            [Tooltip("How much the fog low down takes the map's glow colour (city neon from below): 1 at night, low by day.")]
            [Range(0f, 1f)] public float FogGlow = 1f;
            [Tooltip("Planned: street lamps, floodlights and window glow on.")] public bool CityLights = true;
        }

        [Serializable]
        public sealed class WeatherLook
        {
            public Weather Weather = Weather.Clear;
            [Tooltip("x the map's height fog density (city fog pass).")] [Min(0f)] public float FogDensityScale = 1f;
            [Tooltip("Fog colour this weather pulls towards; alpha = how much (city fog pass).")] public Color FogColour = new(0f, 0f, 0f, 0f);
            [Tooltip("Planned: wet ground and puddles (0 = dry).")] [Range(0f, 1f)] public float Wetness;
            [Tooltip("Planned: particles that follow the camera (rain, sand, snow, ash). Empty = none.")] public GameObject Effect;
        }

        [SerializeField] private List<TimeOfDayLook> times = new();
        [SerializeField] private List<WeatherLook> weathers = new();

        /// <summary>Volumetric fog near the camera (Buto package, Chopper.Presentation.Buto). The map's height fog and the
        /// conditions above feed it; the full-screen height fog carries on past <see cref="Range"/>.</summary>
        [Serializable]
        public sealed class VolumetricFogLook
        {
            [Tooltip("Use volumetric fog when the Buto package is in the project.")] public bool Enabled = true;
            [Tooltip("Metres of volumetric fog from the camera; the height fog pass takes over beyond.")] [Min(10f)] public float Range = 450f;
            [Tooltip("Buto depth ratio: higher = more grid slices near the camera.")] [Min(0.5f)] public float DepthRatio = 2.5f;
            [Tooltip("Buto grid cell size in screen pixels (bigger = cheaper, blurrier).")] [Min(3)] public int GridPixelSize = 12;
            [Tooltip("Buto grid slices along the view.")] [Range(32, 240)] public int GridSlices = 128;
            [Tooltip("x the map's fog density (1/m) to Buto's density.")] [Min(0f)] public float DensityScale = 1.5f;
            [Tooltip("Forward scattering (0..1): light glows round lamps and the sun when you look towards them.")]
            [Range(-1f, 1f)] public float Anisotropy = 0.45f;
            [Tooltip("x light in the fog.")] [Min(0f)] public float LightIntensity = 1.2f;
            [Tooltip("Self glow of the fog from the map's glow colour (city neon from below), x the time of day's Fog Glow.")]
            [Min(0f)] public float GlowEmission = 0.35f;
            [Tooltip("Fog in shadow, x the lit fog colour.")] [Range(0f, 1f)] public float ShadowShade = 0.35f;
            [Tooltip("City point lights that light the fog: the nearest this many to the camera (Buto takes 16 at most).")]
            [Range(0, 16)] public int MaxLights = 12;
        }

        [SerializeField] private VolumetricFogLook volumetric = new();
        public VolumetricFogLook Volumetric => volumetric;

        [Header("Editor preview (ignored in builds)")]
        [SerializeField, Tooltip("Editor only: show these conditions in play mode, whatever the server picked (cosmetic).")]
        private bool previewOverride;
        [SerializeField] private TimeOfDay previewTime = TimeOfDay.Day;
        [SerializeField] private Weather previewWeather = Weather.Clear;

        /// <summary>True in the editor when the preview override is on.</summary>
        public bool TryGetPreview(out TimeOfDay time, out Weather weather)
        {
            time = previewTime;
            weather = previewWeather;
            return Application.isEditor && previewOverride;
        }

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
