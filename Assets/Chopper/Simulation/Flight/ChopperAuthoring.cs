using Unity.Entities;
using UnityEngine;

namespace Chopper.Simulation.Flight
{
    /// <summary>
    /// Put on the chopper ghost prefab together with a Rigidbody (useGravity off) and colliders.
    /// Defaults match the tuning of the legacy NGO chopper.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ChopperAuthoring : MonoBehaviour
    {
        [Header("Thrust and torque")]
        [SerializeField] private float thrustForce = 13000f;
        [SerializeField, Range(0f, 1f)] private float thrustForwardOffset = 1f;
        [SerializeField] private float yawTorque = 1500f;
        [SerializeField] private float pitchTorque = 1600f;
        [SerializeField] private float rollTorque = 600f;

        [Header("Gravity")]
        [SerializeField] private float gravityAltitude = 5f;
        [SerializeField, Range(-1f, 1f)] private float gravityUprightThreshold = 0.25f;

        [Header("Stabiliser")]
        [SerializeField, Tooltip("km/h")] private float stabiliserMaxSpeedKmh = 50f;
        [SerializeField] private float stabiliserStrength = 2f;

        [Header("Dash")]
        [SerializeField] private float dashBoost = 0.6f;
        [SerializeField] private float dashCooldown = 2f;

        private sealed class Baker : Baker<ChopperAuthoring>
        {
            public override void Bake(ChopperAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new ChopperFlightConfig
                {
                    ThrustForce = authoring.thrustForce,
                    ThrustForwardOffset = authoring.thrustForwardOffset,
                    YawTorque = authoring.yawTorque,
                    PitchTorque = authoring.pitchTorque,
                    RollTorque = authoring.rollTorque,
                    GravityAltitude = authoring.gravityAltitude,
                    GravityUprightThreshold = authoring.gravityUprightThreshold,
                    StabiliserMaxSpeed = authoring.stabiliserMaxSpeedKmh / 3.6f,
                    StabiliserStrength = authoring.stabiliserStrength,
                    DashBoost = authoring.dashBoost,
                    DashCooldown = authoring.dashCooldown,
                });
                AddComponent<ChopperFlightState>(entity);
                AddComponent<ChopperInput>(entity);
            }
        }
    }
}
