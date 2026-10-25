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
        /// <summary>Extra lift from full collective, as a fraction of hover lift (0.6 = 1.6 g up, 0.4 g down).</summary>
        public float CollectiveRange;

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
