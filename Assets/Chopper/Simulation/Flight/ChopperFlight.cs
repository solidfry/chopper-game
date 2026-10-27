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
        /// <summary>Extra rotor lift at full collective (W), m/s², along the rotor axis.</summary>
        public float CollectiveUp;
        /// <summary>Lift taken away at full S, m/s².</summary>
        public float CollectiveDown;
        /// <summary>
        /// How much rotor lift grows with tilt to hold height (0 = real rotor, loses height when tilted;
        /// 1 = Battlefield-style, tilt becomes pure horizontal thrust). Capped at <see cref="MaxCompensatedTilt"/>.
        /// </summary>
        public float TiltCompensation;
        /// <summary>Cosine of the largest tilt that is still compensated (0.5 = 60 degrees).</summary>
        public float MaxCompensatedTilt;
        /// <summary>Multiplies the horizontal push from tilt.</summary>
        public float TiltThrust;
        /// <summary>Part of the tilt push that pitch gives: 1 = nose down accelerates (Battlefield), 0 = pitch only aims.</summary>
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

        /// <summary>How fast the rotor spools to the collective input (1/s). Lower = heavier, higher = snappier.</summary>
        public float CollectiveResponse;

        /// <summary>Boost (held): extra thrust along the nose, m/s².</summary>
        public float BoostAcceleration;
        /// <summary>Speed added along the nose at once when a boost starts, m/s.</summary>
        public float BoostKick;
        /// <summary>Forward drag multiplier while boosting. Below 1 = a higher top speed.</summary>
        public float BoostDragScale;
        /// <summary>Seconds of boost from a full tank.</summary>
        public float BoostDuration;
        /// <summary>Seconds to refill an empty tank.</summary>
        public float BoostRechargeTime;
        /// <summary>Seconds after a boost before the tank refills.</summary>
        public float BoostRechargeDelay;
        /// <summary>Tank needed to start a boost (0..1), so tapping on an empty tank does not stutter.</summary>
        public float BoostStartEnergy;
    }

    /// <summary>Predicted flight state. Ghost fields so the server corrects the client on mismatch.</summary>
    public struct ChopperFlightState : IComponentData
    {
        /// <summary>Boost tank used: 0 = full (the default), 1 = empty.</summary>
        [GhostField(Quantization = 1000)] public float BoostUsed;
        [GhostField(Quantization = 100)] public float BoostRechargeWait;
        [GhostField] public bool Boosting;
        /// <summary>Spooled collective, -1..1 (follows the W/S input at <see cref="ChopperFlightConfig.CollectiveResponse"/>).</summary>
        [GhostField(Quantization = 1000)] public float Collective;
        [GhostField(Quantization = 10)] public float Altitude;

        public float BoostEnergy => 1f - BoostUsed;
    }
}
