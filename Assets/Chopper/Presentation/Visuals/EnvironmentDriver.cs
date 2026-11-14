using Chopper.Simulation.Visuals;
using UnityEngine;

namespace Chopper.Presentation.Visuals
{
    using Camera = UnityEngine.Camera;

    /// <summary>What the environment should show: the match's conditions, their looks, and the view camera.</summary>
    public readonly struct EnvironmentRequest
    {
        public readonly TimeOfDay Time;
        public readonly Weather Weather;
        /// <summary>From <see cref="EnvironmentSettings"/>; may be null when the settings have no look.</summary>
        public readonly EnvironmentSettings.TimeOfDayLook TimeLook;
        public readonly EnvironmentSettings.WeatherLook WeatherLook;
        public readonly Camera Camera;

        public EnvironmentRequest(TimeOfDay time, Weather weather, EnvironmentSettings.TimeOfDayLook timeLook,
            EnvironmentSettings.WeatherLook weatherLook, Camera camera)
        {
            Time = time;
            Weather = weather;
            TimeLook = timeLook;
            WeatherLook = weatherLook;
            Camera = camera;
        }
    }

    /// <summary>
    /// Shows the sky, sun, clouds and weather for the match conditions. One per match scene (setup places it); the
    /// client <see cref="EnvironmentSystem"/> finds it and calls it. Subclass it to plug in a sky asset (UniStorm
    /// driver: <c>Presentation/UniStorm</c>, its own assembly). With no driver in the scene, the system applies the
    /// plain <see cref="EnvironmentSettings"/> look (sun light and ambient) itself.
    /// Client presentation only: never in a subscene; the server never calls it.
    /// </summary>
    public abstract class EnvironmentDriver : MonoBehaviour
    {
        /// <summary>Show these conditions. Called when they arrive or change.</summary>
        public abstract void Apply(in EnvironmentRequest request);

        /// <summary>The view camera changed (spawn, camera rig swap). Sky effects follow it.</summary>
        public virtual void Follow(Camera camera) { }
    }
}
