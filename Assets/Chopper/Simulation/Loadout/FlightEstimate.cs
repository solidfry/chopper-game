using Chopper.Simulation.Flight;
using Unity.Mathematics;

namespace Chopper.Simulation.Loadout
{
    /// <summary>
    /// Flight numbers of a chassis with its modules, in real units. The builder writes the chopper's flight config from
    /// the same functions, and the hangar shows them (top speed, turn rate, climb, boost), so the hangar shows what
    /// flies. Pure.
    /// </summary>
    public static class FlightEstimate
    {
        private const float Gravity = 9.81f;

        /// <summary>Forward drag thinned by the speed multiplier: the top speed scales by it (quadratic part by its square).</summary>
        public static float3 QuadraticDrag(FlightTuning flight, in LoadoutStats stats)
        {
            var drag = (float3)flight.QuadraticDrag;
            drag.z /= Square(stats.SpeedMultiplier);
            return drag;
        }

        public static float3 LinearDrag(FlightTuning flight, in LoadoutStats stats)
        {
            var drag = (float3)flight.LinearDrag;
            drag.z /= stats.SpeedMultiplier;
            return drag;
        }

        public static float BoostPower(FlightTuning flight, in LoadoutStats stats) => flight.BoostAcceleration * stats.BoostPowerMultiplier;

        /// <summary>
        /// Level top speed (m/s) at the largest compensated tilt, without boost: the tilt push (see
        /// <see cref="ChopperFlightSystem"/>) against the forward drag. For comparing loadouts; pilots who dive or boost go faster.
        /// </summary>
        public static float TopSpeed(FlightTuning flight, in LoadoutStats stats)
        {
            var tilt = math.radians(flight.MaxCompensatedTiltDegrees);
            var hoverLift = Gravity * flight.GravityScale * math.lerp(1f, 1f / math.cos(tilt), flight.TiltCompensation);
            var push = hoverLift * math.sin(tilt) * flight.TiltThrust;
            var quadratic = QuadraticDrag(flight, stats).z;
            var linear = LinearDrag(flight, stats).z;
            if (quadratic <= 1e-6f) return linear > 1e-6f ? push / linear : 0f;
            return (-linear + math.sqrt(linear * linear + 4f * quadratic * push)) / (2f * quadratic);
        }

        /// <summary>Yaw rate at full input, degrees per second.</summary>
        public static float TurnRate(FlightTuning flight, in LoadoutStats stats) => flight.MaxYawRate * stats.TurnRateMultiplier;

        /// <summary>Extra lift at full collective, m/s².</summary>
        public static float Climb(FlightTuning flight, in LoadoutStats stats) => flight.CollectiveUp * stats.LiftMultiplier;

        /// <summary>Seconds to refill an empty boost tank.</summary>
        public static float BoostRefill(FlightTuning flight, in LoadoutStats stats) => flight.BoostRechargeTime * stats.BoostRechargeMultiplier;

        private static float Square(float value) => value * value;
    }
}
