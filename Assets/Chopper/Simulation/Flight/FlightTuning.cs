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
        [Header("Collective (W/S): Battlefield")]
        [Tooltip("Extra rotor lift at full W, m/s², along the rotor axis: level = rise, nose down = rise and go forward.")]
        public float CollectiveUp = 12f;
        [Tooltip("Lift taken away at full S, m/s² (sink).")]
        public float CollectiveDown = 18f;
        [Tooltip("How fast the rotor spools to the W/S input (1/s). Lower = heavier, higher = snappier.")]
        public float CollectiveResponse = 6f;

        [Header("Weight and lift")]
        [Tooltip("Gravity multiplier. Above 1 = heavier; drops feel punchier.")]
        public float GravityScale = 1.6f;
        [Range(0f, 1f), Tooltip("Lift grows with tilt to hold height. 1 = height holds at any tilt (up to the max).")]
        public float TiltCompensation = 1f;
        [Range(10f, 80f), Tooltip("Largest tilt (degrees) that is still compensated.")]
        public float MaxCompensatedTiltDegrees = 60f;
        [Range(0f, 3f), Tooltip("Multiplies the horizontal push from tilt. Above 1 = a small tilt gives real speed.")]
        public float TiltThrust = 1.6f;
        [Range(0f, 1f), Tooltip("Part of the tilt push that pitch gives: 1 = Battlefield (nose down = forward), 0 = pitch only aims.")]
        public float PitchThrust = 1f;

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

        [Header("Boost (held, uses a tank)")]
        [Tooltip("Extra thrust where the nose aims while boosting, m/s² (aim down = dive, aim up = climb).")]
        public float BoostAcceleration = 24f;
        [Tooltip("Speed added at once when a boost starts, m/s (the kick).")]
        public float BoostKick = 9f;
        [Range(0.1f, 1f), Tooltip("Forward drag multiplier while boosting. Below 1 = a higher top speed.")]
        public float BoostDragScale = 0.55f;
        [Tooltip("Seconds of boost from a full tank.")]
        public float BoostDuration = 3.5f;
        [Tooltip("Seconds to refill an empty tank. Engine modules scale it.")]
        public float BoostRechargeTime = 6f;
        [Tooltip("Seconds after a boost before the tank refills.")]
        public float BoostRechargeDelay = 0.8f;
        [Range(0f, 1f), Tooltip("Tank needed to start a boost.")]
        public float BoostStartEnergy = 0.2f;

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
