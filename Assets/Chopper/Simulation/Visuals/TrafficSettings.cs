using System.Collections.Generic;
using Chopper.Simulation.Config;
using Unity.Collections;
using UnityEngine;

namespace Chopper.Simulation.Visuals
{
    /// <summary>
    /// How much cosmetic city traffic to show (every map). Setup step 8 makes <c>Data/Rendering/TrafficSettings.asset</c>
    /// once and links it to each city scene's traffic; the numbers are baked with the city scene, so press step 8 (or
    /// reimport the city SubScene) after a change.
    /// </summary>
    [CreateAssetMenu(menuName = "Chopper/Rendering/Traffic Settings", fileName = "TrafficSettings")]
    public sealed class TrafficSettings : ScriptableObject, IValidatedAsset
    {
        [Header("Vehicles per km of path")]
        [SerializeField, Min(0f)] private float street = 7f;
        [SerializeField, Min(0f)] private float main = 12f;
        [SerializeField, Min(0f), Tooltip("Roads out of the city to the tunnels and the canyon.")] private float highway = 5f;
        [SerializeField, Min(0f)] private float freeway = 16f;
        [SerializeField, Min(0f), Tooltip("The sky-road loop.")] private float sky = 9f;
        [SerializeField, Min(0f), Tooltip("The sky-road spiral ramp.")] private float spiral = 5f;
        [SerializeField, Min(0f), Tooltip("Flying-car lanes over the main roads.")] private float air = 5f;
        [SerializeField, Min(0f), Tooltip("Drone loops round the tall towers.")] private float drone = 8f;

        [Header("Look")]
        [SerializeField, Range(0f, 1f), Tooltip("Share of ground vehicles that are trucks.")] private float truckShare = 0.18f;
        [SerializeField, Range(0f, 0.5f), Tooltip("Each vehicle's speed: the path's speed ± this share.")] private float speedJitter = 0.15f;
        [SerializeField, Min(50f), Tooltip("Vehicles farther than this from the camera are not drawn (m).")] private float drawDistance = 1800f;
        [SerializeField, Min(0), Tooltip("Most vehicles on a map; densities scale down to fit.")] private int maxVehicles = 1500;
        [SerializeField, Min(0f), Tooltip("Drones bob up and down this much (m).")] private float droneBob = 0.4f;
        [SerializeField, Range(0f, 60f), Tooltip("Flying cars bank into turns, at most this much (degrees).")] private float flyerBank = 25f;
        [SerializeField, Min(0f), Tooltip("Open paths: vehicles grow in and shrink out over this distance at the ends (m).")]
        private float endFade = 6f;

        [Header("Junctions and queues")]
        [SerializeField, Min(2f), Tooltip("Signal cycle (s): phase 0 green from the start, phase 1 from the middle.")]
        private float signalCycle = 24f;
        [SerializeField, Min(1f), Tooltip("Green time of each phase (s); at most half the cycle. The rest is red for both.")]
        private float signalGreen = 10f;
        [SerializeField, Min(0.5f), Tooltip("m/s² when pulling away.")] private float acceleration = 3f;
        [SerializeField, Min(0.5f), Tooltip("m/s² when stopping for a red light or a queue.")] private float braking = 6f;
        [SerializeField, Min(2f), Tooltip("Gap kept to the vehicle in front, nose to nose (m).")] private float minGap = 9f;

        [Header("Moving lights")]
        [SerializeField, Min(0f), Tooltip("Flying cars' tail-light trails: seconds of flight they show (0 = none).")]
        private float trailSeconds = 0.35f;
        [SerializeField, Min(10f), Tooltip("Searchlight beam length (m).")] private float beamLength = 650f;
        [SerializeField, Min(1f), Tooltip("Searchlight beam radius at its far end (m).")] private float beamRadius = 30f;

        public TrafficLook ToLook()
        {
            var look = new TrafficLook
            {
                TruckShare = truckShare,
                SpeedJitter = speedJitter,
                DrawDistance = drawDistance,
                MaxVehicles = maxVehicles,
                DroneBob = droneBob,
                FlyerBank = flyerBank,
                EndFade = endFade,
                SignalCycle = signalCycle,
                SignalGreen = Mathf.Min(signalGreen, signalCycle * 0.5f),
                Acceleration = acceleration,
                Braking = braking,
                MinGap = minGap,
                TrailSeconds = trailSeconds,
                BeamLength = beamLength,
                BeamRadius = beamRadius,
            };
            foreach (var value in new[] { street, main, highway, freeway, sky, spiral, air, drone }) look.PerKm.Add(value);
            return look;
        }

        /// <summary>The numbers above, for a city scene without a settings asset.</summary>
        public static TrafficLook DefaultLook()
        {
            var defaults = CreateInstance<TrafficSettings>();
            var look = defaults.ToLook();
            if (Application.isPlaying) Destroy(defaults);
            else DestroyImmediate(defaults);
            return look;
        }

        public void CollectProblems(List<string> problems)
        {
            if (maxVehicles <= 0) problems.Add($"{name}: max vehicles is 0, so the cities have no traffic.");
        }
    }
}
