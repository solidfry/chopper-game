using System;
using UnityEngine;

namespace Chopper.Simulation.Flight
{
    /// <summary>
    /// Flight feel of one chassis (Battlefield-style model, see <see cref="ChopperFlightSystem"/>). Lives on the
    /// <see cref="Loadout.ChassisDefinition"/>; modules then scale it (<see cref="Loadout.LoadoutStats"/>).
    /// Defaults are the tuned Little Bird feel.
    /// </summary>
    [Serializable]
    public sealed class FlightTuning
    {
        [Header("Throttle (W/S): fly where you aim")]
        [Tooltip("Push along the nose at full W, m/s². Aim up + W climbs, aim down + W dives. Sets top speed with drag.")]
        public float ForwardThrust = 14f;
        [Tooltip("Push backwards at full S (brake, then reverse), m/s².")]
        public float ReverseThrust = 8f;

        [Header("Weight and lift")]
        [Tooltip("Gravity multiplier. Above 1 = heavier; drops feel punchier.")]
        public float GravityScale = 1.6f;
        [Range(0f, 1f), Tooltip("Lift grows with tilt to hold height. 1 = height holds at any tilt (up to the max).")]
        public float TiltCompensation = 1f;
        [Range(10f, 80f), Tooltip("Largest tilt (degrees) that is still compensated.")]
        public float MaxCompensatedTiltDegrees = 60f;
        [Range(0f, 3f), Tooltip("Sideways push from banking: makes turns curve. 0 = none.")]
        public float TiltThrust = 1.2f;
        [Range(0f, 1f), Tooltip("Part of the bank push that nose-up/down also gives (0 = pitch only aims, 1 = real helicopter: nose down accelerates).")]
        public float PitchThrust;

        [Header("Rotation (degrees per second at full input)")]
        public float MaxPitchRate = 90f;
        public float MaxYawRate = 70f;
        public float MaxRollRate = 140f;
        [Tooltip("How fast rotation reaches the requested rate. Higher = snappier.")]
        public float TurnResponse = 12f;

        [Header("Drag (local axes: x sideways, y vertical, z forward)")]
        public Vector3 QuadraticDrag = new(0.004f, 0.003f, 0.0016f);
        public Vector3 LinearDrag = new(0.15f, 0.15f, 0.02f);
        [Tooltip("Nose turns into the direction of travel at speed.")]
        public float Weathervane = 0.01f;
        [Tooltip("Flight assist: levels the chopper when there is no pitch/roll input. 0 = off (Battlefield default).")]
        public float AutoLevel;

        [Header("Coordinated turn (when there is no roll input)")]
        [Range(0f, 60f), Tooltip("Bank angle at full yaw input. The chopper leans into its turns, so they curve. 0 = off.")]
        public float BankAngleDegrees = 30f;
        [Tooltip("How fast it reaches that bank (1/s).")]
        public float BankResponse = 4f;

        [Header("Dash")]
        public float DashBoost = 0.6f;
        public float DashCooldown = 2f;

        public FlightTuning Clone() => (FlightTuning)MemberwiseClone();
    }

    /// <summary>Crash damage of one chassis. See <see cref="Combat.CrashDamageConfig"/>.</summary>
    [Serializable]
    public sealed class CrashTuning
    {
        [Tooltip("Speed loss in one tick (m/s) that does no crash damage.")]
        public float SafeSpeedLoss = 8f;
        [Tooltip("Crash damage per m/s of speed loss above the safe value.")]
        public float DamagePerSpeedLoss = 4f;
    }
}
