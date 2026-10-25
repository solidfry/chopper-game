using Unity.Entities;
using Unity.NetCode;

namespace Chopper.Simulation.Flight
{
    /// <summary>
    /// Flight tuning for one chopper. Baked from <see cref="ChopperAuthoring"/>; later this comes from the chassis + modules.
    /// </summary>
    public struct ChopperFlightConfig : IComponentData
    {
        public float ThrustForce;
        /// <summary>How far thrust tilts forward from the rotor axis (0 = straight up, 1 = 45 degrees).</summary>
        public float ThrustForwardOffset;
        public float YawTorque;
        public float PitchTorque;
        public float RollTorque;

        /// <summary>Below this height above ground, gravity is on so the chopper can land.</summary>
        public float GravityAltitude;
        /// <summary>Gravity is on when dot(up, world up) is below this, so an upside-down chopper falls.</summary>
        public float GravityUprightThreshold;

        /// <summary>The stabiliser levels roll and pitch below this speed (m/s).</summary>
        public float StabiliserMaxSpeed;
        /// <summary>Angular acceleration per radian of tilt.</summary>
        public float StabiliserStrength;

        /// <summary>Fraction of current velocity added by a dash.</summary>
        public float DashBoost;
        public float DashCooldown;
    }

    /// <summary>Predicted flight state. Ghost fields so the server corrects the client on mismatch.</summary>
    public struct ChopperFlightState : IComponentData
    {
        [GhostField(Quantization = 100)] public float DashCooldownRemaining;
        [GhostField(Quantization = 10)] public float Altitude;
        [GhostField] public bool StabiliserActive;
    }
}
