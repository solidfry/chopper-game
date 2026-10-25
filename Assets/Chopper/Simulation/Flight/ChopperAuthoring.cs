using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Chopper.Simulation.Flight
{
    /// <summary>
    /// Put on the chopper ghost prefab together with a Rigidbody (useGravity on, low damping) and colliders.
    /// Defaults aim for a Battlefield-style attack helicopter: weighty, crisp rotation, ~300 km/h top speed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ChopperAuthoring : MonoBehaviour
    {
        [Header("Lift")]
        [SerializeField, Range(0f, 1.5f), Tooltip("Extra lift at full collective, as a fraction of hover lift.")]
        private float collectiveRange = 0.7f;

        [Header("Rotation (degrees per second at full input)")]
        [SerializeField] private float maxPitchRate = 80f;
        [SerializeField] private float maxYawRate = 70f;
        [SerializeField] private float maxRollRate = 110f;
        [SerializeField, Tooltip("How fast rotation reaches the requested rate. Higher = crisper.")]
        private float turnResponse = 7f;

        [Header("Drag (local axes: x sideways, y vertical, z forward)")]
        [SerializeField] private Vector3 quadraticDrag = new(0.006f, 0.004f, 0.0011f);
        [SerializeField] private Vector3 linearDrag = new(0.4f, 0.3f, 0.05f);
        [SerializeField, Tooltip("Nose turns into the direction of travel at speed.")]
        private float weathervane = 0.015f;
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
