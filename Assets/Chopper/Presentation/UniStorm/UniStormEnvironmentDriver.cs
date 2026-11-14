using System;
using System.Collections.Generic;
using Chopper.Presentation.Visuals;
using Chopper.Simulation.Visuals;
using UniStorm;
using UnityEngine;

namespace Chopper.Presentation.Sky
{
    using Camera = UnityEngine.Camera;

    /// <summary>
    /// <see cref="EnvironmentDriver"/> for the UniStorm asset (sky, sun, moon, clouds, precipitation, lightning).
    /// On the first <see cref="Apply"/> it makes the UniStorm system from <see cref="systemPrefab"/>, set up before it
    /// wakes: no time flow, no weather generation, no UniStorm menu or music, the view camera as its player, and the
    /// match's hour and weather. Later changes go through <c>UniStormManager</c>. The scene's own sun is turned off
    /// (UniStorm brings its own). Setup step 10 places this in each match scene and fills the weather table (<see cref="FillDefaults"/>).
    /// Its own assembly (<c>UNISTORM_PRESENT</c>), so the asset can be removed without breaking the game.
    /// </summary>
    public sealed class UniStormEnvironmentDriver : EnvironmentDriver
    {
        [Serializable]
        public sealed class WeatherBinding
        {
            public Weather Weather;
            public WeatherType Type;
        }

        [SerializeField, Tooltip("UniStorm's URP system prefab (UniStorm Weather System/Resources/Systems/Resources).")]
        private GameObject systemPrefab;
        [SerializeField, Tooltip("UniStorm weather type per match weather. A missing one uses the first entry.")]
        private List<WeatherBinding> weathers = new();
        [SerializeField, Tooltip("Hour (0-23) for Morning, Day, Afternoon and Night.")]
        private int[] hours = { 7, 12, 17, 23 };
        [SerializeField, Range(0f, 1f), Tooltip("UniStorm's own weather sounds (rain, wind, thunder). Its music and time-of-day ambience stay off.")]
        private float weatherSoundVolume = 0.8f;

        private UniStormSystem _system;
        private EnvironmentRequest _pending;
        private bool _hasPending;
        private Camera _camera;

        /// <summary>UniStorm weather type asset names used for each match weather by <see cref="FillDefaults"/>.</summary>
        private static readonly (Weather Weather, string Asset)[] DefaultTypes =
        {
            (Weather.Clear, "Clear"), (Weather.Rain, "Rain"), (Weather.Sandstorm, "Dust Storm"),
            (Weather.Fog, "Foggy"), (Weather.Snow, "Snow"), (Weather.Ash, "Hazy"),
        };

        private const string PrefabResource = "UniStorm URP System";

        private void Reset() => FillDefaults();

        /// <summary>Editor: links UniStorm's URP prefab and a weather type per match weather (by asset name) where
        /// missing. Setup step 10 calls it. Tune the table on the component afterwards.</summary>
        public void FillDefaults()
        {
#if UNITY_EDITOR
            if (systemPrefab == null) systemPrefab = Resources.Load<GameObject>(PrefabResource);
            weathers ??= new List<WeatherBinding>();
            foreach (var (weather, asset) in DefaultTypes)
            {
                if (weathers.Exists(b => b != null && b.Weather == weather && b.Type != null)) continue;
                WeatherType found = null;
                foreach (var guid in UnityEditor.AssetDatabase.FindAssets($"t:{nameof(WeatherType)} {asset}"))
                {
                    var type = UnityEditor.AssetDatabase.LoadAssetAtPath<WeatherType>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                    if (type != null && type.name == asset) { found = type; break; }
                }
                weathers.RemoveAll(b => b == null || b.Weather == weather);
                if (found != null) weathers.Add(new WeatherBinding { Weather = weather, Type = found });
            }
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        public override void Apply(in EnvironmentRequest request)
        {
            if (request.Camera != null) _camera = request.Camera;
            if (_system == null)
            {
                _pending = request;
                _hasPending = true;
                TryCreate();
                return;
            }
            if (!_system.UniStormInitialized)
            {
                _pending = request;
                _hasPending = true;
                return;
            }
            var manager = UniStormManager.Instance;
            if (manager == null) return;
            manager.SetTime(Hour(request.Time), 0);
            var type = TypeFor(request.Weather);
            if (type != null) manager.ChangeWeatherInstantly(type);
        }

        public override void Follow(Camera camera)
        {
            if (camera == null || camera == _camera && _system != null) return;
            _camera = camera;
            if (_system == null)
            {
                if (_hasPending) TryCreate();
                return;
            }
            if (_system.UniStormInitialized && UniStormManager.Instance != null && _system.PlayerCamera != camera)
                UniStormManager.Instance.ChangeCameraSource(camera.transform, camera);
        }

        private void Update()
        {
            // Conditions that came while UniStorm was still starting up.
            if (_hasPending && _system != null && _system.UniStormInitialized)
            {
                _hasPending = false;
                Apply(_pending);
            }
        }

        /// <summary>Makes the UniStorm system once a camera exists (UniStorm needs its player camera at start).</summary>
        private void TryCreate()
        {
            if (_system != null || _camera == null) return;
            if (systemPrefab == null) systemPrefab = Resources.Load<GameObject>(PrefabResource);
            if (systemPrefab == null)
            {
                Debug.LogWarning($"{name}: no UniStorm system prefab. Run Chopper → Setup, step 10.", this);
                return;
            }

            // The scene's baked sun would light the city twice; the camera clears to UniStorm's sky box.
            if (RenderSettings.sun != null) RenderSettings.sun.gameObject.SetActive(false);
            _camera.clearFlags = CameraClearFlags.Skybox;

            // Made under an inactive holder, so it does not wake before it is set up (the prefab asset is not touched).
            var holder = new GameObject("UniStorm (setting up)");
            holder.SetActive(false);
            holder.transform.SetParent(transform, false);
            var instance = Instantiate(systemPrefab, holder.transform);
            instance.name = "UniStorm";
            _system = instance.GetComponent<UniStormSystem>();
            if (_system == null)
            {
                Debug.LogError($"{name}: the prefab {systemPrefab.name} has no UniStormSystem.", this);
                Destroy(holder);
                return;
            }

            var request = _pending;
            _system.GetPlayerAtRuntime = UniStormSystem.EnableFeature.Disabled;
            _system.PlayerTransform = _camera.transform;
            _system.PlayerCamera = _camera;
            _system.UseUniStormMenu = UniStormSystem.EnableFeature.Disabled;
            _system.TimeFlow = UniStormSystem.EnableFeature.Disabled;
            _system.WeatherGeneration = UniStormSystem.EnableFeature.Disabled;
            _system.StartingHour = Hour(request.Time);
            _system.StartingMinute = 0;
            _system.MusicVolume = 0f;
            _system.AmbienceVolume = 0f;
            _system.WeatherSoundsVolume = weatherSoundVolume;
            var type = TypeFor(request.Weather);
            if (type != null)
            {
                _system.CurrentWeatherType = type;
                if (!_system.AllWeatherTypes.Contains(type)) _system.AllWeatherTypes.Add(type);
            }
            instance.SetActive(true);
            instance.transform.SetParent(transform, false);
            Destroy(holder);
            // Applied again once it says it is ready (Update), in case its start-up overwrote anything.
            _hasPending = true;
        }

        private int Hour(TimeOfDay time)
        {
            var index = (int)time;
            return hours != null && index < hours.Length ? Mathf.Clamp(hours[index], 0, 23) : 12;
        }

        private WeatherType TypeFor(Weather weather)
        {
            if (weathers == null || weathers.Count == 0) return null;
            foreach (var binding in weathers)
                if (binding != null && binding.Weather == weather && binding.Type != null)
                    return binding.Type;
            return weathers[0]?.Type;
        }
    }
}
