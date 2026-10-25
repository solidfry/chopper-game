using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Chopper.Simulation.Flight
{
    /// <summary>
    /// Put on the chopper ghost prefab together with a Rigidbody (useGravity on, low damping, Interpolate) and colliders.
    /// Defaults aim for an aggressive Battlefield 6 Little Bird: snappy rotation, heavy momentum, ~300 km/h top speed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ChopperAuthoring : MonoBehaviour
    {
        [Header("Weight and lift")]
        [SerializeField, Tooltip("Gravity multiplier. Above 1 = heavier; drops and climbs feel punchier.")]
        private float gravityScale = 1.6f;
        [SerializeField, Range(0f, 1.5f), Tooltip("Extra lift at full collective, as a fraction of hover lift.")]
        private float collectiveRange = 0.9f;

        [Header("Rotation (degrees per second at full input)")]
        [SerializeField] private float maxPitchRate = 70f;
        [SerializeField] private float maxYawRate = 55f;
        [SerializeField] private float maxRollRate = 100f;
        [SerializeField, Tooltip("How fast rotation reaches the requested rate. Higher = snappier.")]
        private float turnResponse = 9f;

        [Header("Drag (local axes: x sideways, y vertical, z forward)")]
        [SerializeField] private Vector3 quadraticDrag = new(0.004f, 0.003f, 0.0018f);
        [SerializeField] private Vector3 linearDrag = new(0.15f, 0.15f, 0.02f);
        [SerializeField, Tooltip("Nose turns into the direction of travel at speed.")]
        private float weathervane = 0.01f;
        [SerializeField, Tooltip("Flight assist: levels the chopper when there is no pitch/roll input. 0 = off (Battlefield default).")]
        private float autoLevel;

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
                    GravityScale = authoring.gravityScale,
                    CollectiveRange = authoring.collectiveRange,
                    MaxTurnRate = math.radians(new float3(authoring.maxPitchRate, authoring.maxYawRate, authoring.maxRollRate)),
                    TurnResponse = authoring.turnResponse,
                    QuadraticDrag = authoring.quadraticDrag,
                    LinearDrag = authoring.linearDrag,
                    Weathervane = authoring.weathervane,
                    AutoLevel = authoring.autoLevel,
                    DashBoost = authoring.dashBoost,
                    DashCooldown = authoring.dashCooldown,
                });
                AddComponent<ChopperFlightState>(entity);
                AddComponent<ChopperInput>(entity);
            }
        }
    }
}
