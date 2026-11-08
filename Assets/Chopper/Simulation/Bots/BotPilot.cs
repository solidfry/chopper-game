using Chopper.Simulation.Flight;
using Unity.Entities;
using Unity.Mathematics;

namespace Chopper.Simulation.Bots
{
    /// <summary>
    /// What the bot's brain wants from its hands this tick: where to go, how fast, where to point the nose. The
    /// <see cref="BotPilot"/> turns it into stick input.
    /// </summary>
    public struct PilotOrder : IComponentData
    {
        /// <summary>The next waypoint (world space).</summary>
        public float3 Destination;
        /// <summary>Cruise speed to it, m/s. The pilot brakes in time to stop at <see cref="ArriveRadius"/>.</summary>
        public float Speed;
        public float ArriveRadius;
        /// <summary>Point the nose (and the gun) at <see cref="AimPoint"/>; else the nose follows the travel direction.</summary>
        public bool Aim;
        public float3 AimPoint;
        public bool AllowBoost;
        public bool Fire;
        public bool FireSecondary;
    }

    /// <summary>The closest obstacle on the look-ahead (see <see cref="BotSensing.Probe"/>).</summary>
    public struct ObstacleSense
    {
        /// <summary>Distance to the hit, m; -1 = the look-ahead is clear.</summary>
        public float Distance;
        public float3 Normal;
        /// <summary>Length of the look-ahead that hit, m.</summary>
        public float ProbeLength;
        /// <summary>Seconds until impact at the current closing speed.</summary>
        public float TimeToImpact;

        public bool Hit => Distance >= 0f;
        public static ObstacleSense Clear => new() { Distance = -1f, TimeToImpact = float.MaxValue };
    }

    /// <summary>What the pilot knows about its own chopper this tick.</summary>
    public struct PilotSense
    {
        public float3 Position;
        public quaternion Rotation;
        public float3 Velocity;
        /// <summary>Height over the ground or roof below, m (<see cref="ChopperFlightState.Altitude"/>).</summary>
        public float Altitude;
        /// <summary>Boost tank, 0..1.</summary>
        public float BoostEnergy;
        public bool Boosting;
        /// <summary>Gravity, m/s² (positive).</summary>
        public float Gravity;
        public ObstacleSense Obstacle;
        public bool HasBounds;
        public float3 BoundsCentre;
        public float3 BoundsHalfExtents;
    }

    /// <summary>The bot's hands: stick positions move at <see cref="BotHandling.HandSpeed"/>, like a person's.</summary>
    public struct PilotHands
    {
        public float Thrust;
        public float Pitch;
        public float Roll;
        public float Yaw;
        public bool Boost;
    }

    /// <summary>
    /// Flies a chopper to a <see cref="PilotOrder"/> through <see cref="ChopperInput"/> only, so a bot has the same
    /// controls, limits and feel as a player. It inverts the flight model (<see cref="ChopperFlightSystem"/>):
    /// <list type="bullet">
    /// <item>A wanted velocity: to the waypoint at cruise speed, braking in time; height by a climb rate.</item>
    /// <item>Obstacles (look-ahead hit) and the arena bounds bend the wanted velocity away and up.</item>
    /// <item>Horizontal: the speed error is an acceleration, and tilt gives acceleration (hover lift is compensated, so
    /// the push is weight × TiltThrust × tan(tilt)). That gives the wanted rotor axis.</item>
    /// <item>Rotation: the tilt error (rotor axis) and the heading error, or the full error to the nose on the aim point,
    /// is a turn rate; input = rate / max turn rate. The coordinated-turn assist is taken out first.</item>
    /// <item>Collective: the climb rate error is a vertical acceleration.</item>
    /// </list>
    /// Pure function: no world access (sensing is in <see cref="BotSensing"/>).
    /// </summary>
    public static class BotPilot
    {
        /// <summary>Below this wanted speed (m/s) the nose keeps facing the waypoint, not the travel direction.</summary>
        private const float FaceTravelSpeed = 4f;
        /// <summary>The lowest rotor-up value used to scale the collective (steep tilt).</summary>
        private const float MinUpright = 0.35f;

        public static ChopperInput Steer(in PilotOrder order, in PilotSense sense, in ChopperFlightConfig flight,
            in BotHandling handling, ref PilotHands hands, float deltaTime)
        {
            var worldUp = math.up();
            var desired = DesiredVelocity(order, sense, handling);

            // Horizontal: speed error -> acceleration -> tilt of the rotor axis.
            var weight = sense.Gravity * flight.GravityScale;
            var pushPerTan = math.max(weight * flight.TiltThrust, 0.1f);
            var maxAcceleration = pushPerTan * math.tan(handling.MaxTilt);
            var acceleration = Flat(desired - sense.Velocity) * handling.VelocityGain;
            var accelerationLength = math.length(acceleration);
            if (accelerationLength > maxAcceleration) acceleration *= maxAcceleration / accelerationLength;
            var targetUp = math.normalize(worldUp + acceleration / pushPerTan);

            var forward = math.mul(sense.Rotation, math.forward());
            float3 rate;
            if (order.Aim)
            {
                // Nose on the aim point: the full attitude error in local axes -> wanted turn rate.
                var targetRotation = AimAttitude(order.AimPoint - sense.Position, forward, acceleration, pushPerTan);
                var error = RotationVector(math.mul(math.inverse(sense.Rotation), targetRotation));
                rate = error * new float3(handling.AttitudeGain, handling.YawGain, handling.AttitudeGain);
            }
            else
            {
                // Tilt and heading apart: a big heading change (yaw is slow) must not leak into pitch and roll.
                rate = TiltRate(sense.Rotation, targetUp) * handling.AttitudeGain;
                rate.y += HeadingError(forward, Face(order, sense, desired, forward)) * handling.YawGain;
            }

            // Wanted turn rate -> stick (rate / max rate, like the keyboard).
            var maxRate = math.max(flight.MaxTurnRate, 1e-3f);
            var yaw = math.clamp(rate.y / maxRate.y, -1f, 1f);
            var roll = math.clamp(-rate.z / maxRate.z, -1f, 1f);
            // The flight model banks into yaw by itself (coordinated turn): ask for that much less roll.
            var assist = FlightAssists.BankAssistRollRate(sense.Rotation, yaw, roll, flight);
            roll = math.clamp(-(rate.z - assist) / maxRate.z, -1f, 1f);
            var pitch = math.clamp(rate.x / maxRate.x, -1f, 1f);

            // Collective: climb rate error -> vertical acceleration along the (tilted) rotor axis.
            var up = math.mul(sense.Rotation, worldUp);
            var upright = math.max(math.dot(up, worldUp), MinUpright);
            var vertical = (desired.y - sense.Velocity.y) * handling.VerticalGain;
            var thrust = vertical >= 0f
                ? vertical / math.max(flight.CollectiveUp * upright, 0.1f)
                : vertical / math.max(flight.CollectiveDown * upright, 0.1f);

            var boost = WantsBoost(order, sense, handling, hands.Boost, forward);

            // Hands: the sticks move at a human speed.
            var step = handling.HandSpeed * deltaTime;
            hands.Thrust = MoveTowards(hands.Thrust, math.clamp(thrust, -1f, 1f), step);
            hands.Pitch = MoveTowards(hands.Pitch, pitch, step);
            hands.Roll = MoveTowards(hands.Roll, roll, step);
            hands.Yaw = MoveTowards(hands.Yaw, yaw, step);
            hands.Boost = boost;

            return new ChopperInput
            {
                Thrust = hands.Thrust,
                Pitch = hands.Pitch,
                Roll = hands.Roll,
                Yaw = hands.Yaw,
                Boost = boost,
                Fire = order.Fire,
                FireSecondary = order.FireSecondary,
            };
        }

        /// <summary>The velocity the bot wants now: to the waypoint, then bent away from obstacles and the bounds.</summary>
        public static float3 DesiredVelocity(in PilotOrder order, in PilotSense sense, in BotHandling handling)
        {
            var worldUp = math.up();
            var toDestination = order.Destination - sense.Position;
            var flat = Flat(toDestination);
            var flatDistance = math.length(flat);
            var room = math.max(flatDistance - order.ArriveRadius, 0f);
            var direction = flatDistance > 1e-3f ? flat / flatDistance : float3.zero;
            // On a travel leg, faster than cruise (after a boost) is kept: drag slows the chopper, not a hard flare.
            var cruise = order.AllowBoost ? math.max(order.Speed, math.dot(Flat(sense.Velocity), direction)) : order.Speed;
            var speed = math.min(cruise, math.sqrt(2f * handling.BrakeAcceleration * room));
            var desired = direction * speed;
            desired.y = math.clamp(toDestination.y * handling.AltitudeGain, -handling.MaxSink, handling.MaxClimb);

            // Floor: never sink below the minimum clearance over whatever is below.
            if (sense.Altitude < handling.MinClearance)
                desired.y = math.max(desired.y, math.min(handling.MaxClimb, (handling.MinClearance - sense.Altitude) * handling.AltitudeGain * 2f));

            var obstacle = sense.Obstacle;
            if (obstacle.Hit)
            {
                var normal = obstacle.Normal;
                var urgency = math.saturate(1f - obstacle.Distance / math.max(obstacle.ProbeLength, 1f));
                // No speed into the obstacle, then a push away from it and up.
                var into = math.dot(desired, normal);
                if (into < 0f) desired -= normal * into;
                desired += (normal * handling.AvoidPush + worldUp * handling.AvoidClimb) * urgency;
                if (obstacle.TimeToImpact < handling.EmergencySeconds)
                {
                    // Emergency: brake, back off along the normal, full climb.
                    var away = Flat(normal);
                    desired = math.normalizesafe(away) * (handling.AvoidPush * 0.5f);
                    desired.y = handling.MaxClimb;
                }
            }

            if (sense.HasBounds)
            {
                // Outside the inner box: steer back in, harder the further out.
                var inner = math.max(sense.BoundsHalfExtents - handling.BoundsMargin, 1f);
                var offset = sense.Position - sense.BoundsCentre;
                var outside = math.max(math.abs(offset) - inner, 0f) * math.sign(offset);
                desired -= outside * 0.5f;
            }
            return desired;
        }

        /// <summary>Nose direction when not aiming: along the wanted travel, else at the waypoint, else as it is.</summary>
        private static float3 Face(in PilotOrder order, in PilotSense sense, float3 desired, float3 forward)
        {
            var travel = Flat(desired);
            if (math.lengthsq(travel) > FaceTravelSpeed * FaceTravelSpeed) return math.normalize(travel);
            var toDestination = Flat(order.Destination - sense.Position);
            if (math.lengthsq(toDestination) > order.ArriveRadius * order.ArriveRadius) return math.normalize(toDestination);
            return math.normalizesafe(Flat(forward), math.forward());
        }

        /// <summary>Local turn rate (per radian of error, x and z only) that brings the rotor axis to <paramref name="targetUp"/>.</summary>
        private static float3 TiltRate(quaternion rotation, float3 targetUp)
        {
            var up = math.mul(rotation, math.up());
            var axis = math.cross(up, targetUp);
            var sinTilt = math.length(axis);
            if (sinTilt < 1e-5f) return float3.zero;
            var angle = math.atan2(sinTilt, math.dot(up, targetUp));
            var local = math.mul(math.inverse(rotation), axis / sinTilt) * angle;
            return new float3(local.x, 0f, local.z);
        }

        /// <summary>Signed flat angle (radians) from the nose to <paramref name="face"/>; positive = turn right.</summary>
        private static float HeadingError(float3 forward, float3 face)
        {
            var nose = math.normalizesafe(Flat(forward), math.forward());
            var target = math.normalizesafe(Flat(face), nose);
            return math.atan2(math.dot(math.cross(nose, target), math.up()), math.dot(nose, target));
        }

        /// <summary>
        /// Nose on the aim point; the sideways part of the wanted acceleration banks the chopper (as a player strafes
        /// while aiming). Speed along the nose then comes from the collective, not from pitch.
        /// </summary>
        private static quaternion AimAttitude(float3 toAim, float3 forward, float3 acceleration, float pushPerTan)
        {
            var nose = math.normalizesafe(toAim, forward);
            var flatNose = math.normalizesafe(Flat(nose), math.forward());
            var sideways = acceleration - flatNose * math.dot(acceleration, flatNose);
            var up = math.normalize(math.up() + sideways / pushPerTan);
            return quaternion.LookRotationSafe(nose, up);
        }

        private static bool WantsBoost(in PilotOrder order, in PilotSense sense, in BotHandling handling, bool boosting, float3 forward)
        {
            if (!order.AllowBoost || sense.Obstacle.Hit || sense.Altitude < handling.BoostMinClearance) return false;
            var toDestination = Flat(order.Destination - sense.Position);
            var distance = math.length(toDestination);
            if (distance < handling.BoostDistance) return false;
            // A new boost only once the last one has coasted down to cruise speed.
            if (!boosting && math.dot(Flat(sense.Velocity), toDestination / distance) > order.Speed * 1.05f) return false;
            // Only when the nose already points down the leg (boost pushes along the nose).
            var aligned = math.dot(math.normalizesafe(Flat(forward)), toDestination / distance) > 0.93f && forward.y > -0.35f;
            // Start on a well filled tank, then hold it until the tank is empty (one long push, as a player does).
            return aligned && (boosting ? sense.BoostEnergy > 0.02f : sense.BoostEnergy > 0.6f);
        }

        /// <summary>Axis × angle (radians) of a rotation, the short way round.</summary>
        public static float3 RotationVector(quaternion rotation)
        {
            var q = rotation.value;
            if (q.w < 0f) q = -q;
            var sinHalf = math.length(q.xyz);
            if (sinHalf < 1e-6f) return 2f * q.xyz;
            var angle = 2f * math.atan2(sinHalf, q.w);
            return q.xyz / sinHalf * angle;
        }

        private static float3 Flat(float3 v) => new(v.x, 0f, v.z);

        private static float MoveTowards(float current, float target, float step) =>
            current + math.clamp(target - current, -step, step);
    }
}
