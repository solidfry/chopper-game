using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Chopper.Simulation.Bots
{
    /// <summary>
    /// How one kind of bot flies (and later fights). The bot uses the chopper like a player: it only writes
    /// <see cref="Flight.ChopperInput"/>. The gameplay subscene's spawner bakes it into a <see cref="BotHandling"/>
    /// singleton (Chopper → Setup, step 4).
    /// </summary>
    [CreateAssetMenu(menuName = "Chopper/Bot Profile", fileName = "BotProfile")]
    public sealed class BotProfile : ScriptableObject
    {
        [SerializeField] private BotPilotTuning pilot = new();

        public BotPilotTuning Pilot => pilot;
    }

    /// <summary>The bot's hands and eyes for flying. See <see cref="BotPilot"/>.</summary>
    [Serializable]
    public sealed class BotPilotTuning
    {
        [Header("Speed")]
        [Tooltip("Cruise speed between waypoints, m/s.")]
        public float CruiseSpeed = 38f;
        [Tooltip("Planned braking before a waypoint, m/s². Lower = slows down earlier.")]
        public float BrakeAcceleration = 7f;
        [Range(10f, 55f), Tooltip("Largest tilt the bot uses to speed up, slow down or turn (degrees). Above 60 the rotor loses height.")]
        public float MaxTiltDegrees = 38f;
        [Tooltip("How fast the bot corrects its speed (1/s). Higher = harder stick work.")]
        public float VelocityGain = 0.9f;

        [Header("Height")]
        [Tooltip("Climb rate per metre of height error (1/s).")]
        public float AltitudeGain = 0.5f;
        [Tooltip("Largest climb rate, m/s.")]
        public float MaxClimb = 16f;
        [Tooltip("Largest sink rate, m/s.")]
        public float MaxSink = 9f;
        [Tooltip("How fast the collective corrects the climb rate (1/s).")]
        public float VerticalGain = 1.6f;
        [Tooltip("Never lower than this over the ground or a roof, m.")]
        public float MinClearance = 22f;

        [Header("Attitude")]
        [Tooltip("Turn rate per radian of pitch and roll error (1/s). Higher = snappier.")]
        public float AttitudeGain = 4.5f;
        [Tooltip("Turn rate per radian of heading error (1/s).")]
        public float YawGain = 2.2f;
        [Tooltip("Full stick travel per second. A human hand: about 4–8. Lower = smoother and slower.")]
        public float HandSpeed = 5f;

        [Header("Obstacles")]
        [Tooltip("Look ahead along the flight path, seconds of travel (plus the braking distance).")]
        public float ProbeSeconds = 2.2f;
        [Tooltip("Shortest look-ahead, m.")]
        public float MinProbeDistance = 35f;
        [Tooltip("Radius of the look-ahead sphere, m (the chopper plus a safety gap).")]
        public float ProbeRadius = 6f;
        [Tooltip("Speed away from an obstacle at the closest look-ahead hit, m/s.")]
        public float AvoidPush = 26f;
        [Tooltip("Climb added at the closest look-ahead hit, m/s (going over is usually the way out in a city).")]
        public float AvoidClimb = 12f;
        [Tooltip("Time to impact (s) below which the bot brakes hard and climbs.")]
        public float EmergencySeconds = 1.1f;
        [Tooltip("Stay this far inside the arena bounds, m.")]
        public float BoundsMargin = 120f;

        [Header("Boost")]
        [Tooltip("Boost on straight legs longer than this, m.")]
        public float BoostDistance = 260f;
        [Tooltip("No boost lower than this over the ground, m.")]
        public float BoostMinClearance = 45f;

        [Header("Wander (until the brain is built)")]
        [Tooltip("Height range over the ground or roofs at the wander waypoints, m.")]
        public Vector2 WanderClearance = new(35f, 110f);
        [Tooltip("A new waypoint after this many seconds, even when the old one was not reached.")]
        public float WanderSeconds = 40f;
        [Tooltip("Waypoint reached inside this distance, m.")]
        public float ArriveRadius = 40f;

        public BotHandling ToHandling() => new()
        {
            CruiseSpeed = CruiseSpeed,
            BrakeAcceleration = math.max(BrakeAcceleration, 0.5f),
            MaxTilt = math.radians(math.clamp(MaxTiltDegrees, 5f, 60f)),
            VelocityGain = VelocityGain,
            AltitudeGain = AltitudeGain,
            MaxClimb = MaxClimb,
            MaxSink = MaxSink,
            VerticalGain = VerticalGain,
            MinClearance = MinClearance,
            AttitudeGain = AttitudeGain,
            YawGain = YawGain,
            HandSpeed = math.max(HandSpeed, 0.5f),
            ProbeSeconds = ProbeSeconds,
            MinProbeDistance = MinProbeDistance,
            ProbeRadius = ProbeRadius,
            AvoidPush = AvoidPush,
            AvoidClimb = AvoidClimb,
            EmergencySeconds = EmergencySeconds,
            BoundsMargin = BoundsMargin,
            BoostDistance = BoostDistance,
            BoostMinClearance = BoostMinClearance,
            WanderClearance = new float2(WanderClearance.x, math.max(WanderClearance.x, WanderClearance.y)),
            WanderSeconds = math.max(WanderSeconds, 1f),
            ArriveRadius = math.max(ArriveRadius, 1f),
        };
    }

    /// <summary>Unmanaged copy of <see cref="BotPilotTuning"/> (angles in radians). A singleton on the spawner.</summary>
    public struct BotHandling : IComponentData
    {
        public float CruiseSpeed;
        public float BrakeAcceleration;
        public float MaxTilt;
        public float VelocityGain;
        public float AltitudeGain;
        public float MaxClimb;
        public float MaxSink;
        public float VerticalGain;
        public float MinClearance;
        public float AttitudeGain;
        public float YawGain;
        public float HandSpeed;
        public float ProbeSeconds;
        public float MinProbeDistance;
        public float ProbeRadius;
        public float AvoidPush;
        public float AvoidClimb;
        public float EmergencySeconds;
        public float BoundsMargin;
        public float BoostDistance;
        public float BoostMinClearance;
        public float2 WanderClearance;
        public float WanderSeconds;
        public float ArriveRadius;
    }
}
