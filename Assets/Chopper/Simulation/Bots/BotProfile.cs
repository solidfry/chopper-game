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
        [SerializeField] private BotSkillTuning skill = new();

        public BotPilotTuning Pilot => pilot;
        public BotSkillTuning Skill => skill;

        public BotHandling ToHandling()
        {
            var handling = pilot.ToHandling();
            skill.Apply(ref handling);
            return handling;
        }
    }

    /// <summary>How well and how the bot fights (see <see cref="BotBrain"/>). Human limits on purpose.</summary>
    [Serializable]
    public sealed class BotSkillTuning
    {
        [Header("Human limits")]
        [Tooltip("Seconds before the bot reacts to a change (new target, a missile, a lock). People: about 0.2–0.4.")]
        public float ReactionSeconds = 0.3f;
        [Tooltip("Aim error when it starts to aim, degrees. It gets smaller while the bot holds the aim.")]
        public float AimErrorDegrees = 3.5f;
        [Tooltip("Seconds of steady aim until the error is at its smallest (a third of the start).")]
        public float AimSettleSeconds = 1.5f;
        [Tooltip("Flare timing: 1 = the definition's time; above 1 = earlier (worse), below 1 = later (better, riskier).")]
        public float FlareTimingScale = 1f;
        [Range(0f, 1f), Tooltip("Chance to forget a gadget or ability when its moment comes.")]
        public float MistakeChance = 0.12f;

        [Header("Eyes")]
        [Tooltip("Sees enemies in line of sight up to this far, m (like the HUD markers).")]
        public float VisionRange = 900f;
        [Tooltip("Knows about enemies this close even behind buildings (rotor noise), m.")]
        public float HearingRange = 220f;

        [Header("Fighting")]
        [Tooltip("Distance the bot tries to hold from its target while shooting, m.")]
        public float PreferredRange = 220f;
        [Tooltip("Closer than this, the bot extends away and turns back for a new pass, m.")]
        public float MinRange = 70f;
        [Tooltip("Speed while circling a target, m/s.")]
        public float EngageSpeed = 24f;
        [Tooltip("How fast it circles the target while shooting (radians of the circle per second).")]
        public float StrafeRate = 0.3f;
        [Tooltip("Aims (nose on target) when the target is inside this angle, degrees; else turns towards it.")]
        public float AimConeDegrees = 30f;
        [Range(0f, 1f), Tooltip("Stops firing at this gun heat, to avoid an overheat (a careful player).")]
        public float GunHeatRelease = 0.8f;

        [Header("Survival")]
        [Range(0f, 1f), Tooltip("Retreats to repair below this health share.")]
        public float RetreatHealth = 0.35f;
        [Range(0f, 1f), Tooltip("Repairs up to this health share before it goes back in.")]
        public float RepairDoneHealth = 0.9f;
        [Tooltip("Distance to run before it stops to repair, m (also: no enemy in sight).")]
        public float RetreatDistance = 450f;

        public void Apply(ref BotHandling h)
        {
            h.ReactionSeconds = math.max(ReactionSeconds, 0.02f);
            h.AimError = math.radians(math.max(AimErrorDegrees, 0f));
            h.AimSettleSeconds = math.max(AimSettleSeconds, 0.1f);
            h.FlareTimingScale = math.max(FlareTimingScale, 0.1f);
            h.MistakeChance = math.saturate(MistakeChance);
            h.VisionRange = VisionRange;
            h.HearingRange = HearingRange;
            h.PreferredRange = math.max(PreferredRange, MinRange + 10f);
            h.MinRange = MinRange;
            h.EngageSpeed = EngageSpeed;
            h.StrafeRate = StrafeRate;
            h.AimConeCos = math.cos(math.radians(math.clamp(AimConeDegrees, 1f, 90f)));
            h.GunHeatRelease = math.saturate(GunHeatRelease);
            h.RetreatHealth = math.saturate(RetreatHealth);
            h.RepairDoneHealth = math.max(math.saturate(RepairDoneHealth), h.RetreatHealth);
            h.RetreatDistance = RetreatDistance;
        }
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

        [Header("Navigation (path finding on the level grid)")]
        [Tooltip("Grid cell size, m. Smaller = tighter gaps, more memory and slower searches. The map's grid uses the first bot's value.")]
        public float NavCellSize = 12f;
        [Tooltip("Paths keep at least this far from walls, m (rounded up to whole cells; 1 cell is already about half a cell from any surface).")]
        public float PathMinClearance = 12f;
        [Tooltip("Paths prefer this much room, m: closer cells cost more.")]
        public float PathPreferredClearance = 40f;
        [Tooltip("Extra cost next to a wall (1 = twice the length), fading to 0 at the preferred clearance.")]
        public float PathClearancePenalty = 1.5f;
        [Tooltip("Extra cost per metre of climb. 0 = goes over everything; higher = goes round buildings.")]
        public float PathClimbCost = 0.4f;
        [Tooltip("Paths stay about this high over street level, m (low flight between buildings).")]
        public float PathCruiseHeight = 30f;
        [Tooltip("Extra cost per 100 m above the cruise height (1 = twice the length). 0 = height does not matter.")]
        public float PathHeightCost = 1.5f;
        [Tooltip("Most grid cells one search may open (CPU limit). Then the path goes to the closest point found.")]
        public int PathMaxNodes = 40000;
        [Tooltip("Plan the path again after this many seconds.")]
        public float ReplanSeconds = 4f;

        [Header("Wander (until the brain is built)")]
        [Tooltip("Height range over the ground or roofs at the wander waypoints, m.")]
        public Vector2 WanderClearance = new(15f, 45f);
        [Tooltip("Share of wander goals inside the city area (the rest anywhere in the bounds).")]
        [Range(0f, 1f)] public float WanderCityShare = 0.8f;
        [Tooltip("Radius of the city area round the arena centre, m.")]
        public float WanderCityRadius = 650f;
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
            WanderCityShare = math.saturate(WanderCityShare),
            WanderCityRadius = math.max(WanderCityRadius, 10f),
            PathCruiseHeight = PathCruiseHeight,
            PathHeightCost = math.max(PathHeightCost, 0f),
            ArriveRadius = math.max(ArriveRadius, 1f),
            NavCellSize = math.max(NavCellSize, 2f),
            PathMinClearance = PathMinClearance,
            PathPreferredClearance = PathPreferredClearance,
            PathClearancePenalty = math.max(PathClearancePenalty, 0f),
            PathClimbCost = math.max(PathClimbCost, 0f),
            PathMaxNodes = PathMaxNodes,
            ReplanSeconds = math.max(ReplanSeconds, 0.5f),
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
        public float WanderCityShare;
        public float WanderCityRadius;
        public float PathCruiseHeight;
        public float PathHeightCost;
        public float WanderSeconds;
        public float ArriveRadius;
        public float NavCellSize;
        public float PathMinClearance;
        public float PathPreferredClearance;
        public float PathClearancePenalty;
        public float PathClimbCost;
        public int PathMaxNodes;
        public float ReplanSeconds;

        // Skill (BotSkillTuning).
        public float ReactionSeconds;
        public float AimError;
        public float AimSettleSeconds;
        public float FlareTimingScale;
        public float MistakeChance;
        public float VisionRange;
        public float HearingRange;
        public float PreferredRange;
        public float MinRange;
        public float EngageSpeed;
        public float StrafeRate;
        public float AimConeCos;
        public float GunHeatRelease;
        public float RetreatHealth;
        public float RepairDoneHealth;
        public float RetreatDistance;
    }
}
