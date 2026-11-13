using Chopper.Presentation.Bridge;
using Chopper.Simulation.Visuals;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;

namespace Chopper.Presentation.Visuals
{
    /// <summary>
    /// Client: applies the match's <see cref="MatchConditions"/> (from the server's map announcement) in match scenes.
    /// Hands them to the scene's <see cref="EnvironmentDriver"/> (sky asset), or with none applies the plain
    /// <see cref="EnvironmentSettings"/> look: sun light colour, strength and angle, ambient trilight, camera background.
    /// Always sets the global shader values <c>_ChopperCityLights</c> (0 or 1: street lamps and window glow on) and
    /// <c>_ChopperWetness</c> (0..1), for city and terrain shaders. In the editor, the settings' preview override wins.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class EnvironmentSystem : PresentationSystemBase
    {
        private static readonly int CityLightsId = Shader.PropertyToID("_ChopperCityLights");
        private static readonly int WetnessId = Shader.PropertyToID("_ChopperWetness");

        private EnvironmentDriver _driver;
        private int _sceneHandle = -1;
        private bool _applied;
        private TimeOfDay _time;
        private Weather _weather;
        private Camera _camera;

        protected override void OnCreate()
        {
            base.OnCreate();
            RequireForUpdate<MatchConditions>();
        }

        protected override void OnUpdate()
        {
            if (!TryGetContext(out var context) || !context.IsMatchScene) return;
            var settings = context.Config != null ? context.Config.Environment : null;
            var conditions = SystemAPI.GetSingleton<MatchConditions>();
            var time = conditions.Time;
            var weather = conditions.Weather;
            if (settings != null && settings.TryGetPreview(out var previewTime, out var previewWeather))
            {
                time = previewTime;
                weather = previewWeather;
            }

            // A new scene: find its driver and apply again.
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            if (scene != _sceneHandle)
            {
                _sceneHandle = scene;
                _driver = Object.FindAnyObjectByType<EnvironmentDriver>();
                _applied = false;
                _camera = null;
            }

            var camera = context.CameraRig != null ? context.CameraRig.GetComponent<Camera>() : Camera.main;
            if (camera != _camera && camera != null)
            {
                _camera = camera;
                if (_driver != null) _driver.Follow(camera);
            }

            if (_applied && time == _time && weather == _weather) return;
            _applied = true;
            _time = time;
            _weather = weather;

            var timeLook = settings != null ? settings.Look(time) : null;
            var weatherLook = settings != null ? settings.Look(weather) : null;
            var request = new EnvironmentRequest(time, weather, timeLook, weatherLook, _camera);
            if (_driver != null) _driver.Apply(request);
            else ApplyPlainLook(request);

            Shader.SetGlobalFloat(CityLightsId, timeLook == null || timeLook.CityLights ? 1f : 0f);
            Shader.SetGlobalFloat(WetnessId, weatherLook != null ? weatherLook.Wetness : 0f);
        }

        /// <summary>No sky asset: the scene's sun and ambient light from the look (as Setup step 10 bakes them).</summary>
        private static void ApplyPlainLook(in EnvironmentRequest request)
        {
            var look = request.TimeLook;
            if (look == null) return;
            var sun = RenderSettings.sun;
            if (sun != null)
            {
                sun.color = look.SunColour;
                sun.intensity = look.SunIntensity;
                sun.transform.rotation = Quaternion.Euler(look.SunPitchYaw.x, look.SunPitchYaw.y, 0f);
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = look.AmbientSky;
            RenderSettings.ambientEquatorColor = look.AmbientEquator;
            RenderSettings.ambientGroundColor = look.AmbientGround;
            if (request.Camera != null && request.Camera.clearFlags == CameraClearFlags.SolidColor)
                request.Camera.backgroundColor = look.Background;
        }
    }
}
