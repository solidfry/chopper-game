using Chopper.Simulation.Bots;
using Chopper.Simulation.Flight;
using NUnit.Framework;
using Unity.Mathematics;

namespace Chopper.Tests
{
    /// <summary>
    /// <see cref="BotPilot"/> against a small copy of the flight model (<see cref="ChopperFlightSystem"/>: compensated
    /// hover lift, tilt push, collective with spool, heading-frame drag, rate controller with the coordinated-turn
    /// assist). No collisions: obstacles are given as an <see cref="ObstacleSense"/>.
    /// </summary>
    public sealed class BotPilotTests
    {
        private const float Gravity = 9.81f;
        private const float Step = 1f / 60f;

        private struct Body
        {
            public float3 Position;
            public quaternion Rotation;
            public float3 Velocity;
            public float3 LocalRate;
            public float Collective;
        }

        private static ChopperFlightConfig Flight()
        {
            var t = new FlightTuning();
            return new ChopperFlightConfig
            {
                GravityScale = t.GravityScale,
                CollectiveUp = t.CollectiveUp,
                CollectiveDown = t.CollectiveDown,
                TiltCompensation = t.TiltCompensation,
                MaxCompensatedTilt = math.cos(math.radians(t.MaxCompensatedTiltDegrees)),
                TiltThrust = t.TiltThrust,
                PitchThrust = t.PitchThrust,
                MaxBank = math.radians(t.BankAngleDegrees),
                BankResponse = t.BankResponse,
                MaxTurnRate = math.radians(new float3(t.MaxPitchRate, t.MaxYawRate, t.MaxRollRate)),
                TurnResponse = t.TurnResponse,
                QuadraticDrag = t.QuadraticDrag,
                LinearDrag = t.LinearDrag,
                CollectiveResponse = t.CollectiveResponse,
            };
        }

        /// <summary>One fixed step of the flight model (no boost, no dive, no weathervane, no auto-level).</summary>
        private static void Integrate(ref Body body, in ChopperInput input, in ChopperFlightConfig c)
        {
            var worldUp = math.up();
            var up = math.mul(body.Rotation, worldUp);
            var weight = Gravity * c.GravityScale;
            var hover = weight;
            var upright = math.dot(up, worldUp);
            if (upright > 0f) hover *= math.lerp(1f, 1f / math.max(upright, c.MaxCompensatedTilt), c.TiltCompensation);
            var lift = up * hover;
            var vertical = worldUp * math.dot(lift, worldUp);
            var nose = math.mul(body.Rotation, math.forward());
            var flatNose = math.normalizesafe(new float3(nose.x, 0f, nose.z), math.forward());
            var push = lift - vertical;
            var along = flatNose * math.dot(push, flatNose);
            push = (push - along + along * c.PitchThrust) * c.TiltThrust;
            body.Velocity += (vertical + push - worldUp * weight) * Step;

            body.Collective += (input.Thrust - body.Collective) * (1f - math.exp(-c.CollectiveResponse * Step));
            body.Velocity += up * ((body.Collective >= 0f ? body.Collective * c.CollectiveUp : body.Collective * c.CollectiveDown) * Step);

            var heading = quaternion.LookRotationSafe(flatNose, worldUp);
            var local = math.mul(math.inverse(heading), body.Velocity);
            var drag = -(c.QuadraticDrag * local * math.abs(local) + c.LinearDrag * local) * Step;
            drag = math.select(drag, -local, math.abs(drag) > math.abs(local));
            body.Velocity += math.mul(heading, drag);

            var desiredRate = c.MaxTurnRate * new float3(input.Pitch, input.Yaw, -input.Roll);
            desiredRate.z += FlightAssists.BankAssistRollRate(body.Rotation, input.Yaw, input.Roll, c);
            body.LocalRate += (desiredRate - body.LocalRate) * (1f - math.exp(-c.TurnResponse * Step));

            var angle = math.length(body.LocalRate) * Step;
            if (angle > 1e-7f) body.Rotation = math.normalize(math.mul(body.Rotation, quaternion.AxisAngle(math.normalize(body.LocalRate), angle)));
            body.Position += body.Velocity * Step;
        }

        private static BotHandling Handling() => new BotPilotTuning().ToHandling();

        private static PilotSense Sense(in Body body) => new()
        {
            Position = body.Position,
            Rotation = body.Rotation,
            Velocity = body.Velocity,
            Altitude = body.Position.y,
            BoostEnergy = 1f,
            Gravity = Gravity,
            Obstacle = ObstacleSense.Clear,
        };

        private static Body Hover(float3 position) => new() { Position = position, Rotation = quaternion.identity };

        [Test]
        public void FliesToAWaypoint_AndStopsThere()
        {
            var flight = Flight();
            var handling = Handling();
            var body = Hover(new float3(0f, 100f, 0f));
            var hands = new PilotHands();
            var order = new PilotOrder { Destination = new float3(400f, 140f, -300f), Speed = handling.CruiseSpeed, ArriveRadius = 10f };
            var maxTilt = 0f;
            var lowest = float.MaxValue;

            for (var i = 0; i < 60 * 45; i++)
            {
                var input = BotPilot.Steer(order, Sense(body), flight, handling, ref hands, Step);
                Integrate(ref body, input, flight);
                maxTilt = math.max(maxTilt, math.degrees(math.acos(math.clamp(math.mul(body.Rotation, math.up()).y, -1f, 1f))));
                lowest = math.min(lowest, body.Position.y);
            }

            Assert.Less(math.distance(body.Position, order.Destination), 25f, $"ended at {body.Position}");
            Assert.Less(math.length(body.Velocity), 4f, "still moving at the waypoint");
            Assert.Less(maxTilt, 60f, "tilted past the compensated range");
            Assert.Greater(lowest, 80f, "lost height on the way");
        }

        [Test]
        public void TurnsAround_ToAWaypointBehind()
        {
            var flight = Flight();
            var handling = Handling();
            var body = Hover(new float3(0f, 80f, 0f));
            var hands = new PilotHands();
            var order = new PilotOrder { Destination = new float3(0f, 80f, -300f), Speed = handling.CruiseSpeed, ArriveRadius = 10f };

            for (var i = 0; i < 60 * 30; i++)
                Integrate(ref body, BotPilot.Steer(order, Sense(body), flight, handling, ref hands, Step), flight);

            Assert.Less(math.distance(body.Position, order.Destination), 25f, $"ended at {body.Position}");
        }

        [Test]
        public void ObstacleAhead_BrakesAndClimbs()
        {
            var flight = Flight();
            var handling = Handling();
            var body = Hover(new float3(0f, 60f, 0f));
            body.Velocity = new float3(0f, 0f, 35f);
            var hands = new PilotHands();
            var order = new PilotOrder { Destination = new float3(0f, 60f, 1000f), Speed = handling.CruiseSpeed, ArriveRadius = 10f };
            var wall = 80f;

            for (var i = 0; i < 60 * 3; i++)
            {
                var sense = Sense(body);
                var distance = wall - body.Position.z;
                if (distance < 100f)
                    sense.Obstacle = new ObstacleSense
                    {
                        Distance = distance,
                        Normal = new float3(0f, 0f, -1f),
                        ProbeLength = 100f,
                        TimeToImpact = distance / math.max(body.Velocity.z, 1f),
                    };
                Integrate(ref body, BotPilot.Steer(order, sense, flight, handling, ref hands, Step), flight);
            }

            Assert.Less(body.Position.z, wall - handling.ProbeRadius, "flew into the wall");
            Assert.Greater(body.Position.y, 60f, "did not climb");
        }

        [Test]
        public void HoldsTheMinimumClearance_WhenTheWaypointIsLow()
        {
            var flight = Flight();
            var handling = Handling();
            var body = Hover(new float3(0f, 60f, 0f));
            var hands = new PilotHands();
            var order = new PilotOrder { Destination = new float3(200f, 0f, 0f), Speed = handling.CruiseSpeed, ArriveRadius = 10f };
            var lowest = float.MaxValue;

            for (var i = 0; i < 60 * 20; i++)
            {
                Integrate(ref body, BotPilot.Steer(order, Sense(body), flight, handling, ref hands, Step), flight);
                lowest = math.min(lowest, body.Position.y);
            }

            Assert.Greater(lowest, handling.MinClearance * 0.6f);
        }

        [Test]
        public void RotationVector_IsAxisTimesAngle()
        {
            var v = BotPilot.RotationVector(quaternion.AxisAngle(math.up(), 0.5f));
            Assert.AreEqual(0.5f, v.y, 1e-4f);
            Assert.AreEqual(0f, v.x, 1e-4f);
            var back = BotPilot.RotationVector(quaternion.AxisAngle(math.up(), -2.5f));
            Assert.AreEqual(-2.5f, back.y, 1e-3f);
        }
    }
}
