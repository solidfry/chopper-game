using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Chopper.Simulation.Flight
{
    /// <summary>
    /// Flight tuning for one chopper (Battlefield-style model). Baked from <see cref="ChopperAuthoring"/>;
    /// later this comes from the chassis + modules.
    /// </summary>
    public struct ChopperFlightConfig : IComponentData
    {
        /// <summary>Gravity multiplier for this chopper. Above 1 = heavier, drops and climbs feel punchier.</summary>
        public float GravityScale;
        /// <summary>Push along the nose at full throttle (W), m/s².</summary>
        public float ForwardThrust;
        /// <summary>Push backwards at full reverse (S), m/s².</summary>
        public float ReverseThrust;
        /// <summary>
        /// How much rotor lift grows with tilt to hold height (0 = real rotor, loses height when tilted;
        /// 1 = Battlefield-style, tilt becomes pure horizontal thrust). Capped at <see cref="MaxCompensatedTilt"/>.
        /// </summary>
        public float TiltCompensation;
        /// <summary>Cosine of the largest tilt that is still compensated (0.5 = 60 degrees).</summary>
        public float MaxCompensatedTilt;
        /// <summary>Multiplies the sideways push from banking (turns curve).</summary>
        public float TiltThrust;
        /// <summary>Part of that push that pitch also gives: 0 = pitch only aims, 1 = nose down accelerates.</summary>
        public float PitchThrust;
        /// <summary>Bank angle at full yaw input when there is no roll input, radians (coordinated turn).</summary>
        public float MaxBank;
        /// <summary>How fast the bank is reached (1/s).</summary>
        public float BankResponse;

        /// <summary>Turn rate at full input, local axes (x = pitch, y = yaw, z = roll), radians per second.</summary>
        public float3 MaxTurnRate;
        /// <summary>How fast the turn rate reaches the requested rate (1/s). Higher = crisper.</summary>
        public float TurnResponse;

        /// <summary>Quadratic drag per local axis (x = sideways, y = vertical, z = forward).</summary>
        public float3 QuadraticDrag;
        /// <summary>Linear drag per local axis, settles slow drift.</summary>
        public float3 LinearDrag;

        /// <summary>How strongly the nose turns into the direction of travel at speed.</summary>
        public float Weathervane;

        /// <summary>Levels roll and pitch when the pilot gives no pitch/roll input (0 = off).</summary>
        public float AutoLevel;

        /// <summary>Fraction of current velocity added by a dash.</summary>
        public float DashBoost;
        public float DashCooldown;
    }

    /// <summary>Predicted flight state. Ghost fields so the server corrects the client on mismatch.</summary>
    public struct ChopperFlightState : IComponentData
    {
        [GhostField(Quantization = 100)] public float DashCooldownRemaining;
        [GhostField(Quantization = 10)] public float Altitude;
    }
}
